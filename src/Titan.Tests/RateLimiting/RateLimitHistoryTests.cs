using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;
using StackExchange.Redis;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.RateLimiting;

namespace Titan.Tests.RateLimiting;

/// <summary>
/// Unit tests for RateLimitService historical metrics functionality.
/// </summary>
public class RateLimitHistoryTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<IClusterClient> _clusterClientMock;
    private readonly Mock<IRateLimitConfigGrain> _grainMock;
    private readonly Mock<IOptions<RateLimitingOptions>> _optionsMock;
    private readonly Mock<ILogger<RateLimitService>> _loggerMock;

    public RateLimitHistoryTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _clusterClientMock = new Mock<IClusterClient>();
        _grainMock = new Mock<IRateLimitConfigGrain>();
        _optionsMock = new Mock<IOptions<RateLimitingOptions>>();
        _loggerMock = new Mock<ILogger<RateLimitService>>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);

        _clusterClientMock.Setup(c => c.GetGrain<IRateLimitConfigGrain>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(_grainMock.Object);

        _optionsMock.Setup(o => o.Value).Returns(new RateLimitingOptions
        {
            Enabled = true,
            ConfigCacheSeconds = 30
        });

        // Setup empty server list for GetMetricsAsync (no buckets/timeouts)
        var serverMock = new Mock<IServer>();
        serverMock.Setup(s => s.KeysAsync(
            It.IsAny<int>(), 
            It.IsAny<RedisValue>(), 
            It.IsAny<int>(), 
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());
        
        _redisMock.Setup(r => r.GetServers())
            .Returns([serverMock.Object]);

        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)])]
            });
    }

    private RateLimitService CreateService()
    {
        return new RateLimitServiceTestableHistory(
            _redisMock.Object,
            _clusterClientMock.Object,
            _optionsMock.Object,
            _loggerMock.Object);
    }

    private void EnableMetricsCollection()
    {
        // Update the grain mock to return MetricsCollectionEnabled = true
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)])],
                MetricsCollectionEnabled = true
            });
    }

    [Fact]
    public async Task RecordMetricsSnapshotAsync_WhenEnabled_PushesToRedisList()
    {
        // Arrange
        EnableMetricsCollection();
        var service = CreateService();

        // Act
        await service.RecordMetricsSnapshotAsync();

        // Assert - verify LPUSH and LTRIM were called
        _databaseMock.Verify(d => d.ListLeftPushAsync(
            "rl|history",
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _databaseMock.Verify(d => d.ListTrimAsync(
            "rl|history",
            0,
            299,
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RecordMetricsSnapshotAsync_WhenDisabled_DoesNotPushToRedisList()
    {
        // Arrange - collection disabled by default
        var service = CreateService();

        // Act
        await service.RecordMetricsSnapshotAsync();

        // Assert - verify LPUSH was NOT called
        _databaseMock.Verify(d => d.ListLeftPushAsync(
            "rl|history",
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task IsMetricsCollectionEnabledAsync_WhenNotConfigured_ReturnsFalse()
    {
        // Arrange - default config has MetricsCollectionEnabled = false
        var service = CreateService();

        // Act
        var result = await service.IsMetricsCollectionEnabledAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsMetricsCollectionEnabledAsync_WhenConfigured_ReturnsTrue()
    {
        // Arrange
        EnableMetricsCollection();
        var service = CreateService();

        // Act
        var result = await service.IsMetricsCollectionEnabledAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SetMetricsCollectionEnabledAsync_CallsGrain()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.SetMetricsCollectionEnabledAsync(true);

        // Assert
        _grainMock.Verify(g => g.SetMetricsCollectionEnabledAsync(true), Times.Once);
    }

    [Fact]
    public async Task ClearMetricsHistoryAsync_DeletesRedisKey()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.ClearMetricsHistoryAsync();

        // Assert
        _databaseMock.Verify(d => d.KeyDeleteAsync(
            "rl|history",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_ReturnsDeserializedSnapshots()
    {
        // Arrange
        var snapshot1 = "{\"Timestamp\":\"2025-12-18T00:00:00+00:00\",\"ActiveBuckets\":5,\"ActiveTimeouts\":2,\"TotalRequests\":100}";
        var snapshot2 = "{\"Timestamp\":\"2025-12-18T00:01:00+00:00\",\"ActiveBuckets\":3,\"ActiveTimeouts\":1,\"TotalRequests\":50}";
        
        _databaseMock.Setup(d => d.ListRangeAsync(
            "rl|history",
            0,
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync([snapshot1, snapshot2]);

        var service = CreateService();

        // Act
        var result = await service.GetMetricsHistoryAsync(10);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].ActiveBuckets);
        Assert.Equal(2, result[0].ActiveTimeouts);
        Assert.Equal(100, result[0].TotalRequests);
        Assert.Equal(3, result[1].ActiveBuckets);
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_SkipsMalformedEntries()
    {
        // Arrange
        var validSnapshot = "{\"Timestamp\":\"2025-12-18T00:00:00+00:00\",\"ActiveBuckets\":5,\"ActiveTimeouts\":2,\"TotalRequests\":100}";
        var malformedSnapshot = "not valid json";
        
        _databaseMock.Setup(d => d.ListRangeAsync(
            "rl|history",
            0,
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync([validSnapshot, malformedSnapshot]);

        var service = CreateService();

        // Act
        var result = await service.GetMetricsHistoryAsync(10);

        // Assert - only valid snapshot returned
        Assert.Single(result);
        Assert.Equal(5, result[0].ActiveBuckets);
    }

    [Fact]
    public async Task GetMetricsHistoryAsync_ClampsCountTo300()
    {
        // Arrange
        _databaseMock.Setup(d => d.ListRangeAsync(
            "rl|history",
            0,
            299, // Should be clamped to 300-1
            It.IsAny<CommandFlags>()))
            .ReturnsAsync([]);

        var service = CreateService();

        // Act
        await service.GetMetricsHistoryAsync(500); // Request more than max

        // Assert
        _databaseMock.Verify(d => d.ListRangeAsync(
            "rl|history",
            0,
            299,
            It.IsAny<CommandFlags>()), Times.Once);
    }
}

/// <summary>
/// Testable version of RateLimitService for history tests.
/// </summary>
internal class RateLimitServiceTestableHistory : RateLimitService
{
    public RateLimitServiceTestableHistory(
        IConnectionMultiplexer redis,
        IClusterClient clusterClient,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitService> logger)
        : base(redis, clusterClient, options, logger)
    {
    }
}
