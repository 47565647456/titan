using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Xunit;
using Orleans.TestingHost;

namespace Titan.Tests;

public class SocialGraphTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IGrainFactory _grainFactory => _cluster.GrainFactory;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task AddRelation_ShouldPersist_AndRetrieve()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var socialGrain = _grainFactory.GetGrain<ISocialGrain>(userId);

        // Act
        await socialGrain.AddRelationAsync(friendId, "Friend");

        // Assert
        var relations = await socialGrain.GetRelationsAsync();
        Assert.Single(relations);
        Assert.Equal(friendId, relations[0].TargetUserId);
        Assert.Equal("Friend", relations[0].RelationType);
    }

    [Fact]
    public async Task AddRelation_Duplicate_ShouldBeIgnored()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var socialGrain = _grainFactory.GetGrain<ISocialGrain>(userId);

        // Act
        await socialGrain.AddRelationAsync(friendId, "Friend");
        await socialGrain.AddRelationAsync(friendId, "Friend"); // Duplicate

        // Assert
        var relations = await socialGrain.GetRelationsAsync();
        Assert.Single(relations); // Still only 1
    }

    [Fact]
    public async Task RemoveRelation_ShouldWork()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var friendId = Guid.NewGuid();
        var socialGrain = _grainFactory.GetGrain<ISocialGrain>(userId);
        await socialGrain.AddRelationAsync(friendId, "Friend");

        // Act
        await socialGrain.RemoveRelationAsync(friendId);

        // Assert
        var relations = await socialGrain.GetRelationsAsync();
        Assert.Empty(relations);
    }
}
