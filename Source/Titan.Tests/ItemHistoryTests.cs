using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Xunit;
using Orleans.TestingHost;

namespace Titan.Tests;

public class ItemHistoryTests : IAsyncLifetime
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
    public async Task AddEntry_ShouldAppendToHistory()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);

        // Act
        await historyGrain.AddEntryAsync("Created", actorId, details: "Initial spawn");

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Single(history);
        var entry = history[0];
        Assert.Equal("Created", entry.EventType);
        Assert.Equal(actorId, entry.ActorUserId);
        Assert.Equal("Initial spawn", entry.Details);
        Assert.True(entry.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task MultipleEntries_ShouldMaintainChronologicalOrder()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);

        // Act - Add entries in sequence
        await historyGrain.AddEntryAsync("Created", actorId, details: "Initial creation");
        await Task.Delay(10); // Small delay to ensure distinct timestamps
        await historyGrain.AddEntryAsync("Modified", actorId, details: "Enhanced");
        await Task.Delay(10);
        await historyGrain.AddEntryAsync("Traded", actorId, targetId, "Transferred to new owner");

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Equal(3, history.Count);
        
        // Verify chronological order (oldest first)
        Assert.Equal("Created", history[0].EventType);
        Assert.Equal("Modified", history[1].EventType);
        Assert.Equal("Traded", history[2].EventType);
        
        // Verify timestamps are ascending
        Assert.True(history[0].Timestamp <= history[1].Timestamp);
        Assert.True(history[1].Timestamp <= history[2].Timestamp);
    }

    [Fact]
    public async Task GetHistory_ShouldReturnAllEntries()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);
        var expectedCount = 5;

        // Act - Add multiple entries
        for (int i = 0; i < expectedCount; i++)
        {
            await historyGrain.AddEntryAsync($"Event{i}", Guid.NewGuid(), details: $"Details for event {i}");
        }

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Equal(expectedCount, history.Count);
    }

    [Fact]
    public async Task History_WithDifferentEventTypes_ShouldTrackAll()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var historyGrain = _grainFactory.GetGrain<IItemHistoryGrain>(itemId);

        // Act - Simulate item lifecycle
        await historyGrain.AddEntryAsync("Created", ownerId, details: "Dropped by boss");
        await historyGrain.AddEntryAsync("PickedUp", ownerId, details: "Looted from ground");
        await historyGrain.AddEntryAsync("Equipped", ownerId, details: "Equipped to main hand");
        await historyGrain.AddEntryAsync("Unequipped", ownerId, details: "Removed from main hand");
        await historyGrain.AddEntryAsync("Traded", ownerId, traderId, "Traded for gold");

        // Assert
        var history = await historyGrain.GetHistoryAsync();
        Assert.Equal(5, history.Count);
        
        // Verify we can find specific event types
        Assert.Contains(history, h => h.EventType == "Created");
        Assert.Contains(history, h => h.EventType == "Traded" && h.TargetUserId == traderId);
    }
}
