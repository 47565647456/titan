using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for Character History functionality.
/// </summary>
public class CharacterHistoryTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

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
    public async Task NewCharacter_HasCreatedEvent()
    {
        // Arrange & Act
        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, "standard");
        await charGrain.InitializeAsync(accountId, "HistoryTest", CharacterRestrictions.None);

        // Assert
        var history = await charGrain.GetHistoryAsync();
        
        Assert.Single(history);
        Assert.Equal(CharacterEventTypes.Created, history[0].EventType);
        Assert.Contains("HistoryTest", history[0].Description);
        Assert.Contains("standard", history[0].Description);
    }

    [Fact]
    public async Task HardcoreDeath_RecordsDeathEvent()
    {
        // Arrange - Create HC character in temp season
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var tempSeasonId = $"test-hist-death-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = tempSeasonId,
            Name = "Test History Death",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, tempSeasonId);
        await charGrain.InitializeAsync(accountId, "DeathHistoryTest", CharacterRestrictions.Hardcore);

        // Act - Character dies
        await charGrain.DieAsync();

        // Assert - History contains Created and Died events
        var history = await charGrain.GetHistoryAsync();
        
        Assert.True(history.Count >= 2);
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.Created);
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.Died);
        
        var deathEvent = history.First(h => h.EventType == CharacterEventTypes.Died);
        Assert.Contains("died", deathEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_RecordsMigrationEvent()
    {
        // Arrange - Create character in temp season
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var tempSeasonId = $"test-hist-migrate-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = tempSeasonId,
            Name = "Test History Migration",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, tempSeasonId);
        await charGrain.InitializeAsync(accountId, "MigrationHistoryTest", CharacterRestrictions.None);

        // Act - Migrate character
        await charGrain.MigrateToSeasonAsync("standard");

        // Assert - History contains migration event
        var history = await charGrain.GetHistoryAsync();
        
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.Migrated);
        
        var migrateEvent = history.First(h => h.EventType == CharacterEventTypes.Migrated);
        Assert.NotNull(migrateEvent.Data);
        Assert.Equal(tempSeasonId, migrateEvent.Data!["sourceSeasonId"]);
        Assert.Equal("standard", migrateEvent.Data["targetSeasonId"]);
    }

    [Fact]
    public async Task MigrationWithRestrictionChange_RecordsRestrictionsChanged()
    {
        // Arrange - Create HC character in temp season
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var tempSeasonId = $"test-hist-restrict-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = tempSeasonId,
            Name = "Test Restriction Change",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, tempSeasonId);
        await charGrain.InitializeAsync(accountId, "RestrictionHistoryTest", CharacterRestrictions.Hardcore);

        // Act - HC character dies (triggers migration with restriction change)
        await charGrain.DieAsync();

        // Assert - History contains RestrictionsChanged event
        var history = await charGrain.GetHistoryAsync();
        
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.RestrictionsChanged);
        
        var restrictEvent = history.First(h => h.EventType == CharacterEventTypes.RestrictionsChanged);
        Assert.NotNull(restrictEvent.Data);
        Assert.Contains("Hardcore", restrictEvent.Data!["previousRestrictions"].ToString()!);
    }

    [Fact]
    public async Task History_IsOrderedByTimestamp()
    {
        // Arrange - Create HC character and trigger multiple events
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var tempSeasonId = $"test-hist-order-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = tempSeasonId,
            Name = "Test History Order",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, tempSeasonId);
        await charGrain.InitializeAsync(accountId, "OrderTest", CharacterRestrictions.Hardcore);
        await charGrain.DieAsync(); // This triggers: Died, Migrated, RestrictionsChanged

        // Assert - Events are in chronological order
        var history = await charGrain.GetHistoryAsync();
        
        Assert.True(history.Count >= 2);
        
        for (int i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].Timestamp >= history[i - 1].Timestamp,
                $"Event {i} should be after event {i - 1}");
        }
    }

    [Fact]
    public async Task AddHistoryEntryAsync_AddsCustomEvent()
    {
        // Arrange
        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, "standard");
        await charGrain.InitializeAsync(accountId, "CustomEventTest", CharacterRestrictions.None);

        // Act - Add custom event
        await charGrain.AddHistoryEntryAsync(
            "CustomEvent",
            "Something special happened",
            new Dictionary<string, string> { ["customData"] = "test" });

        // Assert
        var history = await charGrain.GetHistoryAsync();
        
        Assert.Contains(history, h => h.EventType == "CustomEvent");
        
        var customEvent = history.First(h => h.EventType == "CustomEvent");
        Assert.Equal("Something special happened", customEvent.Description);
        Assert.Equal("test", customEvent.Data!["customData"]);
    }
}
