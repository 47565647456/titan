using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for Void League functionality - seasons where characters and items
/// do NOT migrate to Standard when the season ends or on Hardcore death.
/// </summary>
public class VoidLeagueTests : IAsyncLifetime
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
    public async Task VoidSeason_CanCreate()
    {
        // Arrange
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var voidSeasonId = $"void-{Guid.NewGuid():N}";

        // Act
        var voidSeason = await registry.CreateSeasonAsync(new Season
        {
            SeasonId = voidSeasonId,
            Name = "Void League Test",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = true
        });

        // Assert
        Assert.True(voidSeason.IsVoid);
        Assert.Equal(SeasonType.Temporary, voidSeason.Type);
    }

    [Fact]
    public async Task VoidSeason_HardcoreDeath_ShouldNotMigrate()
    {
        // Arrange - Create a void league
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var voidSeasonId = $"void-hc-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = voidSeasonId,
            Name = "Void HC League",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = true
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, voidSeasonId);
        await charGrain.InitializeAsync(accountId, "VoidHardcorePlayer", CharacterRestrictions.Hardcore);

        // Act - Character dies in void league
        var deadCharacter = await charGrain.DieAsync();

        // Assert - Character is dead but NOT migrated (this is the key difference from normal leagues)
        Assert.True(deadCharacter.IsDead);
        Assert.False(deadCharacter.IsMigrated);
        
        // Verify character does NOT exist in standard
        var standardCharGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, "standard");
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            standardCharGrain.GetCharacterAsync());
    }

    [Fact]
    public async Task VoidSeason_StartMigration_ShouldThrow()
    {
        // Arrange - Create a void league and end it
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var voidSeasonId = $"void-migrate-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = voidSeasonId,
            Name = "Void Migration Test League",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = true
        });

        // End the season first (required before migration)
        await registry.EndSeasonAsync(voidSeasonId);

        // Act & Assert - Attempting to start bulk migration should throw
        var migrationGrain = _cluster.GrainFactory.GetGrain<ISeasonMigrationGrain>(voidSeasonId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            migrationGrain.StartMigrationAsync("standard"));

        Assert.Contains("Void League", ex.Message);
        Assert.Contains("not preserved", ex.Message);
    }

    [Fact]
    public async Task VoidSeason_EndSeason_CharactersRemainInSeason()
    {
        // Arrange - Create void league with a character
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var voidSeasonId = $"void-end-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = voidSeasonId,
            Name = "Void End Test League",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = true
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, voidSeasonId);
        await charGrain.InitializeAsync(accountId, "VoidPlayer", CharacterRestrictions.None);

        // Act - End the season
        await registry.EndSeasonAsync(voidSeasonId);

        // Assert - Character still exists in void season
        var character = await charGrain.GetCharacterAsync();
        Assert.Equal("VoidPlayer", character.Name);
        Assert.Equal(voidSeasonId, character.SeasonId);
        Assert.False(character.IsMigrated);
    }

    [Fact]
    public async Task NonVoidSeason_HardcoreDeath_ShouldMigrate()
    {
        // Arrange - Create a normal (non-void) temporary season for comparison
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var normalSeasonId = $"normal-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = normalSeasonId,
            Name = "Normal League",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = false // Explicitly not void
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, normalSeasonId);
        await charGrain.InitializeAsync(accountId, "NormalHardcorePlayer", CharacterRestrictions.Hardcore);

        // Act - Character dies in normal league
        var deadCharacter = await charGrain.DieAsync();

        // Assert - Character IS migrated (normal behavior)
        Assert.True(deadCharacter.IsDead);
        Assert.True(deadCharacter.IsMigrated);
        
        // Verify character exists in standard
        var standardCharGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, "standard");
        var standardChar = await standardCharGrain.GetCharacterAsync();
        Assert.Equal("NormalHardcorePlayer", standardChar.Name);
    }

    [Fact]
    public async Task VoidSeason_SSFHardcore_DeathBehavior()
    {
        // Arrange - Create SSF+HC character in void league
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var voidSeasonId = $"void-ssfhc-{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = voidSeasonId,
            Name = "Void SSF HC League",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard",
            IsVoid = true
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, voidSeasonId);
        var restrictions = CharacterRestrictions.Hardcore | CharacterRestrictions.SoloSelfFound;
        await charGrain.InitializeAsync(accountId, "VoidSSFHC", restrictions);

        // Act - Character dies in void league
        var deadCharacter = await charGrain.DieAsync();

        // Assert - Character stays in void, both flags intact, no migration
        Assert.True(deadCharacter.IsDead);
        Assert.False(deadCharacter.IsMigrated);
        Assert.True(deadCharacter.Restrictions.HasFlag(CharacterRestrictions.Hardcore));
        Assert.True(deadCharacter.Restrictions.HasFlag(CharacterRestrictions.SoloSelfFound));
    }
}
