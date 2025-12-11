using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for SeasonMigrationGrain - bulk migration orchestration.
/// </summary>
public class SeasonMigrationTests : IAsyncLifetime
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
    public async Task StartMigration_SetsStateToInProgress()
    {
        // Arrange - Create and end a season
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var seasonId = $"test-migrate-state-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = seasonId,
            Name = "Test Migration State",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });
        await registry.EndSeasonAsync(seasonId);

        // Act
        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(seasonId);
        var status = await migrationGrain.StartMigrationAsync("standard");

        // Assert
        Assert.Equal(MigrationState.InProgress, status.State);
        Assert.Equal(seasonId, status.SourceSeasonId);
        Assert.Equal("standard", status.TargetSeasonId);
        Assert.NotNull(status.StartedAt);
    }

    [Fact]
    public async Task MigrateCharacter_TracksProgress()
    {
        // Arrange - Create season with characters
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var seasonId = $"test-migrate-progress-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = seasonId,
            Name = "Test Migration Progress",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        // Create characters
        var charId1 = Guid.NewGuid();
        var charId2 = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var char1 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId1, seasonId);
        var char2 = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId2, seasonId);
        await char1.InitializeAsync(accountId, "MigrateProgress1", CharacterRestrictions.None);
        await char2.InitializeAsync(accountId, "MigrateProgress2", CharacterRestrictions.None);

        // End season and start migration
        await registry.EndSeasonAsync(seasonId);
        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(seasonId);
        await migrationGrain.StartMigrationAsync("standard");

        // Act - Migrate first character
        var status1 = await migrationGrain.MigrateCharacterAsync(charId1);
        
        // Assert
        Assert.Equal(1, status1.MigratedCharacters);

        // Act - Migrate second character
        var status2 = await migrationGrain.MigrateCharacterAsync(charId2);
        
        // Assert
        Assert.Equal(2, status2.MigratedCharacters);
    }

    [Fact]
    public async Task CancelMigration_SetsStateToCancelled()
    {
        // Arrange - Start a migration
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var seasonId = $"test-migrate-cancel-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = seasonId,
            Name = "Test Migration Cancel",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });
        await registry.EndSeasonAsync(seasonId);

        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(seasonId);
        await migrationGrain.StartMigrationAsync("standard");

        // Act
        await migrationGrain.CancelMigrationAsync();
        var status = await migrationGrain.GetStatusAsync();

        // Assert
        Assert.Equal(MigrationState.Cancelled, status.State);
        Assert.NotNull(status.CompletedAt);
    }

    [Fact]
    public async Task StartMigration_RequiresEndedSeason()
    {
        // Arrange - Create active season (not ended)
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var seasonId = $"test-migrate-active-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = seasonId,
            Name = "Test Active Migration",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        // Act & Assert - Cannot start migration on active season
        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(seasonId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            migrationGrain.StartMigrationAsync("standard"));

        Assert.Contains("must be ended", ex.Message);
    }

    [Fact]
    public async Task StartMigration_CannotStartTwice()
    {
        // Arrange - Start migration
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var seasonId = $"test-migrate-twice-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = seasonId,
            Name = "Test Double Start",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });
        await registry.EndSeasonAsync(seasonId);

        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(seasonId);
        await migrationGrain.StartMigrationAsync("standard");

        // Act & Assert - Cannot start again
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            migrationGrain.StartMigrationAsync("standard"));

        Assert.Contains("already in progress", ex.Message);
    }
}
