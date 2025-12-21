using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Titan.API.Config;
using Titan.API.Services.Auth;

namespace Titan.Tests;

/// <summary>
/// Unit tests for RedisSessionService.
/// Tests session creation, validation, invalidation, and limit enforcement.
/// </summary>
public class RedisSessionServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<ILogger<RedisSessionService>> _loggerMock;
    private readonly SessionOptions _options;
    private readonly RedisSessionService _service;

    public RedisSessionServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<RedisSessionService>>();
        
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);
        
        _options = new SessionOptions
        {
            KeyPrefix = "session",
            MaxSessionsPerUser = 5,
            SessionLifetimeMinutes = 30,
            SlidingExpirationMinutes = 10
        };
        
        _service = new RedisSessionService(
            _redisMock.Object, 
            Options.Create(_options), 
            _loggerMock.Object);
    }

    #region CreateSessionAsync Tests

    [Fact]
    public async Task CreateSessionAsync_ReturnsValidSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "Mock";
        var roles = new[] { "User" };
        
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());
        _databaseMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CreateSessionAsync(userId, provider, roles);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.TicketId);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(result.TicketId.Length > 20); // Base64Url encoded 32 bytes
        Assert.DoesNotContain('+', result.TicketId); // URL-safe
        Assert.DoesNotContain('/', result.TicketId); // URL-safe
    }

    [Fact]
    public async Task CreateSessionAsync_StoresSessionInRedis()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var provider = "EOS";
        var roles = new[] { "User", "Admin" };
        
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());
        _databaseMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _service.CreateSessionAsync(userId, provider, roles);

        // Assert - verify Redis operations were called
        _databaseMock.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString()!.StartsWith("session:")),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _databaseMock.Verify(db => db.SetAddAsync(
            It.Is<RedisKey>(k => k.ToString()!.StartsWith("session:user:")),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region ValidateSessionAsync Tests

    [Fact]
    public async Task ValidateSessionAsync_ValidSession_ReturnsSession()
    {
        // Arrange
        var ticketId = "test-ticket-id";
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        
        var ticket = new SessionTicket
        {
            UserId = userId,
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-5),
            ExpiresAt = now.AddMinutes(25),
            IsAdmin = false
        };
        
        var serialized = MemoryPackSerializer.Serialize(ticket);
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)serialized);

        // Act
        var result = await _service.ValidateSessionAsync(ticketId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("Mock", result.Provider);
    }

    [Fact]
    public async Task ValidateSessionAsync_ExpiredSession_ReturnsNull()
    {
        // Arrange
        var ticketId = "expired-ticket";
        var now = DateTimeOffset.UtcNow;
        
        var ticket = new SessionTicket
        {
            UserId = Guid.NewGuid(),
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-60),
            ExpiresAt = now.AddMinutes(-30) // Expired
        };
        
        var serialized = MemoryPackSerializer.Serialize(ticket);
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)serialized);

        // Act
        var result = await _service.ValidateSessionAsync(ticketId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSessionAsync_NonExistentSession_ReturnsNull()
    {
        // Arrange
        var ticketId = "nonexistent-ticket";
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _service.ValidateSessionAsync(ticketId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSessionAsync_AppliesSlidingExpiration()
    {
        // Arrange
        var ticketId = "sliding-test";
        var now = DateTimeOffset.UtcNow;
        var slidingMinutes = _options.SlidingExpirationMinutes;
        
        var ticket = new SessionTicket
        {
            UserId = Guid.NewGuid(),
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-5),
            ExpiresAt = now.AddMinutes(5) // Close to expiring
        };
        
        var serialized = MemoryPackSerializer.Serialize(ticket);
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)serialized);
        _databaseMock.Setup(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _service.ValidateSessionAsync(ticketId);

        // Assert - verify sliding expiration was applied via StringSetAsync
        // (implementation uses StringSetAsync to update the ticket, not KeyExpireAsync)
        _databaseMock.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region InvalidateSessionAsync Tests

    [Fact]
    public async Task InvalidateSessionAsync_ExistingSession_DeletesSession()
    {
        // Arrange
        var ticketId = "session-to-invalidate";
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        
        var ticket = new SessionTicket
        {
            UserId = userId,
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30)
        };
        
        var serialized = MemoryPackSerializer.Serialize(ticket);
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)serialized);
        _databaseMock.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.InvalidateSessionAsync(ticketId);

        // Assert
        Assert.True(result);
        _databaseMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString()!.Contains(ticketId)),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateSessionAsync_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var ticketId = "nonexistent";
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _service.InvalidateSessionAsync(ticketId);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region InvalidateAllSessionsAsync Tests

    [Fact]
    public async Task InvalidateAllSessionsAsync_MultipleSessions_DeletesAll()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessions = new RedisValue[] { "session1", "session2", "session3" };
        
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(sessions);
        // Batch delete returns count of deleted keys
        _databaseMock.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);
        _databaseMock.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.InvalidateAllSessionsAsync(userId);

        // Assert
        Assert.Equal(3, result);
        // Verify batch delete of session keys (single call with 3 keys)
        _databaseMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey[]>(keys => keys.Length == 3), 
            It.IsAny<CommandFlags>()), Times.Once);
        // Verify user sessions set key deleted separately
        _databaseMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString()!.Contains("user:")), 
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAllSessionsAsync_NoSessions_ReturnsZero()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await _service.InvalidateAllSessionsAsync(userId);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region URL-Safe Base64 Tests

    [Fact]
    public async Task CreateSessionAsync_GeneratesUrlSafeTicketId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var roles = new[] { "User" };
        
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());
        _databaseMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act - create multiple sessions to test entropy
        var ticketIds = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            var result = await _service.CreateSessionAsync(userId, "Mock", roles);
            ticketIds.Add(result.TicketId);
            
            // URL-safe assertions
            Assert.DoesNotContain('+', result.TicketId);
            Assert.DoesNotContain('/', result.TicketId);
            Assert.DoesNotContain('=', result.TicketId);
        }

        // Assert - all ticket IDs should be unique
        Assert.Equal(10, ticketIds.Count);
    }

    #endregion

    #region Session Limit Enforcement Tests

    [Fact]
    public async Task CreateSessionAsync_ExceedsLimit_EvictsOldestSessions()
    {
        // Arrange - Set limit to 2
        var limitedOptions = new SessionOptions
        {
            KeyPrefix = "session",
            MaxSessionsPerUser = 2,
            SessionLifetimeMinutes = 30,
            SlidingExpirationMinutes = 10
        };
        
        var limitedService = new RedisSessionService(
            _redisMock.Object, 
            Options.Create(limitedOptions), 
            _loggerMock.Object);

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        
        // Simulate 3 existing sessions (oldest first)
        var existingSessions = new RedisValue[] { "old-session-1", "old-session-2", "old-session-3" };
        
        // Create session tickets with timestamps
        var oldTicket1 = new SessionTicket
        {
            UserId = userId,
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-30), // Oldest
            ExpiresAt = now.AddMinutes(30)
        };
        var oldTicket2 = new SessionTicket
        {
            UserId = userId,
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-20),
            ExpiresAt = now.AddMinutes(30)
        };
        var oldTicket3 = new SessionTicket
        {
            UserId = userId,
            Provider = "Mock",
            Roles = new[] { "User" },
            CreatedAt = now.AddMinutes(-10), // Newest existing
            ExpiresAt = now.AddMinutes(30)
        };

        // Setup mocks
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(existingSessions);
        
        // Batch StringGetAsync returns all session data
        _databaseMock.Setup(db => db.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                MemoryPackSerializer.Serialize(oldTicket1),
                MemoryPackSerializer.Serialize(oldTicket2),
                MemoryPackSerializer.Serialize(oldTicket3)
            });
        
        _databaseMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2); // 2 oldest sessions deleted
        _databaseMock.Setup(db => db.SetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        // Act - Create new session (with 3 existing, over limit of 2, should evict 1 oldest)
        // Note: Mock returns only the 3 configured sessions, not the newly added one
        var result = await limitedService.CreateSessionAsync(userId, "Mock", new[] { "User" });

        // Assert
        Assert.NotNull(result);
        
        // Verify batch key delete was called (eviction uses batch delete)
        // Mock returns 3 sessions, with limit=2, evict 3-2=1 oldest session
        _databaseMock.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey[]>(keys => keys.Length == 1), // 1 oldest session evicted
            It.IsAny<CommandFlags>()), Times.Once);
    }


    [Fact]
    public async Task CreateSessionAsync_UnderLimit_DoesNotEvict()
    {
        // Arrange
        var userId = Guid.NewGuid();
        
        // Only 2 existing sessions, limit is 5
        var existingSessions = new RedisValue[] { "session1", "session2" };
        
        _databaseMock.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(existingSessions);
        _databaseMock.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _databaseMock.Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CreateSessionAsync(userId, "Mock", new[] { "User" });

        // Assert
        Assert.NotNull(result);
        
        // Verify batch key delete was NOT called (no eviction needed)
        _databaseMock.Verify(db => db.KeyDeleteAsync(
            It.IsAny<RedisKey[]>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    #endregion
}
