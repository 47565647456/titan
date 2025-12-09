using Orleans.Streams;
using Orleans.TestingHost;
using Titan.Abstractions;
using Titan.Abstractions.Events;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for Orleans Streams trade event publishing.
/// Verifies that trade actions publish the correct events to subscribers.
/// </summary>
public class TradeStreamTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private readonly List<TradeEvent> _receivedEvents = new();
    private StreamSubscriptionHandle<TradeEvent>? _subscription;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_subscription != null)
        {
            await _subscription.UnsubscribeAsync();
        }
        await _cluster.StopAllSilosAsync();
    }

    private async Task SubscribeToTradeStream(Guid tradeId)
    {
        var streamProvider = _cluster.Client.GetStreamProvider(TradeStreamConstants.ProviderName);
        var stream = streamProvider.GetStream<TradeEvent>(
            StreamId.Create(TradeStreamConstants.Namespace, tradeId));

        _subscription = await stream.SubscribeAsync((tradeEvent, token) =>
        {
            _receivedEvents.Add(tradeEvent);
            return Task.CompletedTask;
        });
    }

    private async Task<bool> WaitForEventAsync(string eventType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_receivedEvents.Any(e => e.EventType == eventType))
                return true;
            await Task.Delay(50);
        }
        return _receivedEvents.Any(e => e.EventType == eventType);
    }

    [Fact]
    public async Task InitiateTrade_ShouldPublish_TradeStartedEvent()
    {
        // Arrange - Subscribe FIRST, before any action
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SubscribeToTradeStream(tradeId);

        // Act
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Wait for stream delivery
        var received = await WaitForEventAsync("TradeStarted", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"TradeStarted event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "TradeStarted");
        Assert.Equal(tradeId, evt.TradeId);
        Assert.Equal(userA, evt.UserId);
        Assert.NotNull(evt.Session);
        Assert.Equal(TradeStatus.Pending, evt.Session!.Status);
    }

    [Fact]
    public async Task AddItem_ShouldPublish_ItemAddedEvent()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        
        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var item = await inventoryA.AddItemAsync("StreamTestItem", 1);
        
        // Subscribe BEFORE initiating to catch all events
        await SubscribeToTradeStream(tradeId);
        
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act
        await tradeGrain.AddItemAsync(userA, item.Id);
        var received = await WaitForEventAsync("ItemAdded", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"ItemAdded event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "ItemAdded");
        Assert.Equal(userA, evt.UserId);
        Assert.Equal(item.Id, evt.ItemId);
    }

    [Fact]
    public async Task AcceptTrade_ShouldPublish_TradeAcceptedEvent()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await SubscribeToTradeStream(tradeId);
        
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act - First user accepts
        await tradeGrain.AcceptAsync(userA);
        var received = await WaitForEventAsync("TradeAccepted", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"TradeAccepted event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "TradeAccepted");
        Assert.Equal(userA, evt.UserId);
    }

    [Fact]
    public async Task CompleteTrade_ShouldPublish_TradeCompletedEvent()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var item = await inventoryA.AddItemAsync("CompletionItem", 1);

        await SubscribeToTradeStream(tradeId);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);
        await tradeGrain.AddItemAsync(userA, item.Id);
        await tradeGrain.AcceptAsync(userA);

        // Act - Second user accepts, completing the trade
        await tradeGrain.AcceptAsync(userB);
        var received = await WaitForEventAsync("TradeCompleted", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"TradeCompleted event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "TradeCompleted");
        Assert.Equal(userB, evt.UserId);
        Assert.NotNull(evt.Session);
        Assert.Equal(TradeStatus.Completed, evt.Session!.Status);
    }

    [Fact]
    public async Task CancelTrade_ShouldPublish_TradeCancelledEvent()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await SubscribeToTradeStream(tradeId);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act
        await tradeGrain.CancelAsync(userA);
        var received = await WaitForEventAsync("TradeCancelled", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"TradeCancelled event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "TradeCancelled");
        Assert.Equal(userA, evt.UserId);
    }

    [Fact]
    public async Task ExpiredTrade_ShouldPublish_TradeExpiredEvent()
    {
        // Arrange - Test uses 5 second timeout
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await SubscribeToTradeStream(tradeId);

        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);

        // Act - Wait for expiration (5 second timeout in tests + check interval)
        await Task.Delay(TimeSpan.FromSeconds(7));

        // Trigger session check which will expire and publish
        await tradeGrain.GetSessionAsync();
        var received = await WaitForEventAsync("TradeExpired", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"TradeExpired event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "TradeExpired");
        Assert.Equal(tradeId, evt.TradeId);
        Assert.NotNull(evt.Session);
        Assert.Equal(TradeStatus.Expired, evt.Session!.Status);
    }

    [Fact]
    public async Task RemoveItem_ShouldPublish_ItemRemovedEvent()
    {
        // Arrange
        var tradeId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        
        var inventoryA = _cluster.GrainFactory.GetGrain<IInventoryGrain>(userA);
        var item = await inventoryA.AddItemAsync("RemoveTestItem", 1);
        
        await SubscribeToTradeStream(tradeId);
        
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(tradeId);
        await tradeGrain.InitiateAsync(userA, userB);
        await tradeGrain.AddItemAsync(userA, item.Id);

        // Act
        await tradeGrain.RemoveItemAsync(userA, item.Id);
        var received = await WaitForEventAsync("ItemRemoved", TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(received, $"ItemRemoved event not received. Got: {string.Join(", ", _receivedEvents.Select(e => e.EventType))}");
        var evt = _receivedEvents.First(e => e.EventType == "ItemRemoved");
        Assert.Equal(userA, evt.UserId);
        Assert.Equal(item.Id, evt.ItemId);
    }
}
