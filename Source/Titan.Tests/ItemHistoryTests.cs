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
}
