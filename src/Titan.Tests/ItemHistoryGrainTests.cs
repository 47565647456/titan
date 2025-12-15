using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for ItemHistoryGrain.
/// </summary>
[Collection(ClusterCollection.Name)]
public class ItemHistoryGrainTests
{
    private readonly TestCluster _cluster;

    public ItemHistoryGrainTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    [Fact]
    public async Task RecordEventAsync_AddsEntry()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);

        // Act
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated);

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Single(history);
        Assert.Equal(ItemEventTypes.Generated, history[0].EventType);
    }

    [Fact]
    public async Task RecordEventAsync_WithDetails_IncludesDetails()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        var details = new Dictionary<string, string>
        {
            ["source"] = "monster_drop",
            ["monster_id"] = "skeleton_warrior"
        };

        // Act
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated, details: details);

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Single(history);
        Assert.NotNull(history[0].Details);
        Assert.Equal("monster_drop", history[0].Details!["source"]);
    }

    [Fact]
    public async Task RecordEventAsync_WithActor_IncludesActor()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);

        // Act
        await historyGrain.RecordEventAsync(
            ItemEventTypes.Equipped,
            actorAccountId: accountId,
            actorCharacterId: characterId);

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Single(history);
        Assert.Equal(accountId, history[0].ActorAccountId);
        Assert.Equal(characterId, history[0].ActorCharacterId);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsChronological()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated);
        await Task.Delay(10); // Ensure different timestamps
        await historyGrain.RecordEventAsync(ItemEventTypes.Equipped);
        await Task.Delay(10);
        await historyGrain.RecordEventAsync(ItemEventTypes.Traded);

        // Act
        var history = await historyGrain.GetHistoryAsync();

        // Assert
        Assert.Equal(3, history.Count);
        Assert.Equal(ItemEventTypes.Generated, history[0].EventType);
        Assert.Equal(ItemEventTypes.Equipped, history[1].EventType);
        Assert.Equal(ItemEventTypes.Traded, history[2].EventType);
    }

    [Fact]
    public async Task GetHistoryAsync_WithLimit_ReturnsLimited()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        
        for (int i = 0; i < 10; i++)
        {
            await historyGrain.RecordEventAsync(ItemEventTypes.Moved);
        }

        // Act
        var history = await historyGrain.GetHistoryAsync(limit: 5);

        // Assert
        Assert.Equal(5, history.Count);
    }

    [Fact]
    public async Task GetHistorySinceAsync_FiltersOldEntries()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated);
        await Task.Delay(50);
        var cutoff = DateTimeOffset.UtcNow;
        await Task.Delay(50);
        await historyGrain.RecordEventAsync(ItemEventTypes.Equipped);
        await historyGrain.RecordEventAsync(ItemEventTypes.Traded);

        // Act
        var history = await historyGrain.GetHistorySinceAsync(cutoff);

        // Assert
        Assert.Equal(2, history.Count);
        Assert.All(history, e => Assert.True(e.Timestamp >= cutoff));
    }

    [Fact]
    public async Task GetEntryCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated);
        await historyGrain.RecordEventAsync(ItemEventTypes.Equipped);
        await historyGrain.RecordEventAsync(ItemEventTypes.Unequipped);

        // Act
        var count = await historyGrain.GetEntryCountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task RecordEventAsync_UniqueEventIds()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _cluster.GrainFactory.GetGrain<IItemHistoryGrain>(itemId);
        
        await historyGrain.RecordEventAsync(ItemEventTypes.Generated);
        await historyGrain.RecordEventAsync(ItemEventTypes.Equipped);

        // Act
        var history = await historyGrain.GetHistoryAsync();

        // Assert
        Assert.Equal(2, history.Count);
        Assert.NotEqual(history[0].EventId, history[1].EventId);
    }
}
