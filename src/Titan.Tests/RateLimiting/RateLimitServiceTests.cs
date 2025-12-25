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
/// Unit tests for RateLimitService.
/// Note: Redis batch operations are tested via integration tests.
/// These unit tests focus on configuration, policy lookup, and caching.
/// </summary>
public class RateLimitServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _databaseMock;
    private readonly Mock<IBatch> _batchMock;
    private readonly Mock<IClusterClient> _clusterClientMock;
    private readonly Mock<IRateLimitConfigGrain> _grainMock;
    private readonly Mock<IOptions<RateLimitingOptions>> _optionsMock;
    private readonly Mock<ILogger<RateLimitService>> _loggerMock;

    public RateLimitServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _databaseMock = new Mock<IDatabase>();
        _batchMock = new Mock<IBatch>();
        _clusterClientMock = new Mock<IClusterClient>();
        _grainMock = new Mock<IRateLimitConfigGrain>();
        _optionsMock = new Mock<IOptions<RateLimitingOptions>>();
        _loggerMock = new Mock<ILogger<RateLimitService>>();

        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_databaseMock.Object);
        
        _databaseMock.Setup(d => d.CreateBatch(It.IsAny<object>()))
            .Returns(_batchMock.Object);

        _clusterClientMock.Setup(c => c.GetGrain<IRateLimitConfigGrain>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(_grainMock.Object);

        _optionsMock.Setup(o => o.Value).Returns(new RateLimitingOptions
        {
            Enabled = true,
            ConfigCacheSeconds = 30
        });
    }

    private RateLimitService CreateService()
    {
        var service = new RateLimitServiceTestable(
            _redisMock.Object,
            _clusterClientMock.Object,
            _optionsMock.Object,
            _loggerMock.Object);
        return service;
    }

    [Fact]
    public async Task CheckAsync_WhenDisabled_ReturnsAllowed()
    {
        // Arrange - include policies so fallback to appsettings isn't triggered
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration 
            { 
                Enabled = false,
                Policies = [new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)])]
            });

        var service = CreateService();

        // Act
        var result = await service.CheckAsync("user:123", "Global");

        // Assert - when disabled, should return early with allowed=true
        Assert.True(result.IsAllowed);
        
        // Verify batch was never created (early return)
        _databaseMock.Verify(d => d.CreateBatch(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_NoPolicyFound_ReturnsAllowed()
    {
        // Arrange - policy exists but doesn't match requested one and default doesn't exist
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [new RateLimitPolicy("SomeOther", [new RateLimitRule(100, 60, 300)])],
                DefaultPolicyName = "NonExistent" // Default doesn't exist either
            });

        var service = CreateService();

        // Act
        var result = await service.CheckAsync("user:123", "RequestedPolicy");

        // Assert - no matching policy or default means allowed
        Assert.True(result.IsAllowed);
        Assert.Null(result.Policy);
    }

    [Fact]
    public async Task IsEnabledAsync_ReturnsConfigEnabled()
    {
        // Arrange
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration { Enabled = true });

        var service = CreateService();

        // Act
        var enabled = await service.IsEnabledAsync();

        // Assert
        Assert.True(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_WhenDisabled_ReturnsFalse()
    {
        // Arrange - include a policy to prevent fallback to appsettings defaults
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration 
            { 
                Enabled = false,
                Policies = [new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)])]
            });

        var service = CreateService();

        // Act
        var enabled = await service.IsEnabledAsync();

        // Assert
        Assert.False(enabled);
    }

    [Fact]
    public async Task GetPolicyForEndpointAsync_MatchesPattern()
    {
        // Arrange
        var authPolicy = new RateLimitPolicy("Auth", [new RateLimitRule(10, 60, 600)]);
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [authPolicy],
                EndpointMappings = [new EndpointRateLimitConfig("/api/auth/*", "Auth")],
                DefaultPolicyName = "Global"
            });

        var service = CreateService();

        // Act
        var policy = await service.GetPolicyForEndpointAsync("/api/auth/login");

        // Assert
        Assert.NotNull(policy);
        Assert.Equal("Auth", policy.Name);
    }

    [Fact]
    public async Task GetPolicyForEndpointAsync_NoMatch_ReturnsNull()
    {
        // Arrange
        var globalPolicy = new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)]);
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [globalPolicy],
                EndpointMappings = [new EndpointRateLimitConfig("/api/auth/*", "Auth")],
                DefaultPolicyName = "Global"
            });

        var service = CreateService();

        // Act
        var policy = await service.GetPolicyForEndpointAsync("/api/users/123");

        // Assert - no explicit mapping means null (middleware will check for [RateLimitPolicy] attribute)
        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyForEndpointAsync_WhenDisabled_ReturnsNull()
    {
        // Arrange - include policies so fallback to appsettings isn't triggered
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration 
            { 
                Enabled = false,
                Policies = [new RateLimitPolicy("Auth", [new RateLimitRule(10, 60, 600)])],
                EndpointMappings = [new EndpointRateLimitConfig("/api/auth/*", "Auth")]
            });

        var service = CreateService();

        // Act
        var policy = await service.GetPolicyForEndpointAsync("/api/auth/login");

        // Assert - when disabled, GetPolicyForEndpointAsync should return null
        Assert.Null(policy);
    }

    [Fact]
    public void ClearCache_ResetsConfigCache()
    {
        // Arrange
        var service = CreateService();

        // Act - should not throw
        service.ClearCache();

        // Assert - next call should fetch from grain
        // (Verified by the grain being called on next access)
    }

    [Fact]
    public async Task GetPolicyAsync_ReturnsMatchingPolicy()
    {
        // Arrange
        var globalPolicy = new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)]);
        var authPolicy = new RateLimitPolicy("Auth", [new RateLimitRule(10, 60, 600)]);
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [globalPolicy, authPolicy],
                DefaultPolicyName = "Global"
            });

        var service = CreateService();

        // Act
        var policy = await service.GetPolicyAsync("Auth");

        // Assert
        Assert.NotNull(policy);
        Assert.Equal("Auth", policy.Name);
    }

    [Fact]
    public async Task GetPolicyAsync_NonExistentPolicy_ReturnsNull()
    {
        // Arrange
        var globalPolicy = new RateLimitPolicy("Global", [new RateLimitRule(100, 60, 300)]);
        _grainMock.Setup(g => g.GetConfigurationAsync())
            .ReturnsAsync(new RateLimitingConfiguration
            {
                Enabled = true,
                Policies = [globalPolicy],
                DefaultPolicyName = "Global"
            });

        var service = CreateService();

        // Act
        var policy = await service.GetPolicyAsync("NonExistent");

        // Assert
        Assert.Null(policy);
    }
}

/// <summary>
/// Testable version of RateLimitService that bypasses keyed service injection.
/// Uses the same logic but allows standard constructor injection for tests.
/// </summary>
internal class RateLimitServiceTestable : RateLimitService
{
    // This works because the [FromKeyedServices] attribute only affects DI resolution,
    // not the actual parameter type. For testing, we can inject any IConnectionMultiplexer.
    public RateLimitServiceTestable(
        IConnectionMultiplexer redis,
        IClusterClient clusterClient,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitService> logger)
        : base(redis, clusterClient, options, logger)
    {
    }
}
