using Orleans.TestingHost;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Tests;

/// <summary>
/// Tests for Season-related functionality including SSF restrictions.
/// </summary>
public class SeasonTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private const string TestSeasonId = "standard";

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
    public async Task SeasonRegistry_ShouldHaveStandardSeason()
    {
        // The SeasonRegistryGrain should auto-create a "standard" permanent season
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.GetSeasonAsync("standard");
        
        Assert.NotNull(season);
        Assert.Equal("standard", season!.SeasonId);
        Assert.Equal(SeasonType.Permanent, season.Type);
        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public async Task SSFCharacter_TradeShouldFail()
    {
        // Arrange - Create an SSF character and a normal character
        var ssfCharId = Guid.NewGuid();
        var normalCharId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var ssfChar = _cluster.GrainFactory.GetGrain<ICharacterGrain>(ssfCharId, TestSeasonId);
        var normalChar = _cluster.GrainFactory.GetGrain<ICharacterGrain>(normalCharId, TestSeasonId);

        await ssfChar.InitializeAsync(accountId, "SSFPlayer", CharacterRestrictions.SoloSelfFound);
        await normalChar.InitializeAsync(accountId, "NormalPlayer", CharacterRestrictions.None);

        // Act & Assert - SSF character trying to initiate trade should fail
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(Guid.NewGuid());
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tradeGrain.InitiateAsync(ssfCharId, normalCharId, TestSeasonId));
        
        Assert.Contains("Solo Self-Found", ex.Message);
    }

    [Fact]
    public async Task SSFCharacter_CannotBeTradeTarget()
    {
        // Arrange - Create an SSF character and a normal character
        var ssfCharId = Guid.NewGuid();
        var normalCharId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var ssfChar = _cluster.GrainFactory.GetGrain<ICharacterGrain>(ssfCharId, TestSeasonId);
        var normalChar = _cluster.GrainFactory.GetGrain<ICharacterGrain>(normalCharId, TestSeasonId);

        await ssfChar.InitializeAsync(accountId, "SSFPlayer", CharacterRestrictions.SoloSelfFound);
        await normalChar.InitializeAsync(accountId, "NormalPlayer", CharacterRestrictions.None);

        // Act & Assert - Normal character trying to trade WITH SSF character should fail
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(Guid.NewGuid());
        
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tradeGrain.InitiateAsync(normalCharId, ssfCharId, TestSeasonId));
        
        Assert.Contains("Solo Self-Found", ex.Message);
    }

    [Fact]
    public async Task NormalCharacter_TradeShouldSucceed()
    {
        // Arrange - Create two normal characters
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrainA = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charA, TestSeasonId);
        var charGrainB = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charB, TestSeasonId);

        await charGrainA.InitializeAsync(accountId, "PlayerA", CharacterRestrictions.None);
        await charGrainB.InitializeAsync(accountId, "PlayerB", CharacterRestrictions.None);

        // Act - Trade should succeed
        var tradeGrain = _cluster.GrainFactory.GetGrain<ITradeGrain>(Guid.NewGuid());
        var session = await tradeGrain.InitiateAsync(charA, charB, TestSeasonId);
        
        // Assert
        Assert.NotNull(session);
        Assert.Equal(TradeStatus.Pending, session.Status);
    }

    [Fact]
    public async Task HardcoreCharacter_DieAsync_ShouldMigrate()
    {
        // Arrange - Create a temporary season for testing Hardcore death
        var registry = _cluster.GrainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var tempSeasonId = $"test-s{Guid.NewGuid():N}";
        await registry.CreateSeasonAsync(new Season
        {
            SeasonId = tempSeasonId,
            Name = "Test Season",
            Type = SeasonType.Temporary,
            Status = SeasonStatus.Active,
            StartDate = DateTimeOffset.UtcNow,
            MigrationTargetId = "standard"
        });

        var charId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(charId, tempSeasonId);
        await charGrain.InitializeAsync(accountId, "HardcorePlayer", CharacterRestrictions.Hardcore);

        // Act - Character dies
        var deadCharacter = await charGrain.DieAsync();

        // Assert
        Assert.True(deadCharacter.IsDead);
        Assert.True(deadCharacter.IsMigrated);
    }

    [Fact]
    public async Task Account_CreateCharacter_ShouldAddToCharacterList()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var accountGrain = _cluster.GrainFactory.GetGrain<IAccountGrain>(accountId);

        // Act
        var character = await accountGrain.CreateCharacterAsync(TestSeasonId, "TestHero", CharacterRestrictions.None);

        // Assert
        var characters = await accountGrain.GetCharactersAsync();
        Assert.Single(characters);
        Assert.Equal("TestHero", characters[0].Name);
        Assert.Equal(TestSeasonId, characters[0].SeasonId);
    }

    [Fact]
    public async Task Account_CreateSSFHardcoreCharacter_ShouldHaveBothRestrictions()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var accountGrain = _cluster.GrainFactory.GetGrain<IAccountGrain>(accountId);

        // Act - Create character with both Hardcore and SSF
        var restrictions = CharacterRestrictions.Hardcore | CharacterRestrictions.SoloSelfFound;
        var summary = await accountGrain.CreateCharacterAsync(TestSeasonId, "SSF-HC Hero", restrictions);

        // Assert
        Assert.True(summary.Restrictions.HasFlag(CharacterRestrictions.Hardcore));
        Assert.True(summary.Restrictions.HasFlag(CharacterRestrictions.SoloSelfFound));

        // Verify the character grain also has the restrictions
        var charGrain = _cluster.GrainFactory.GetGrain<ICharacterGrain>(summary.CharacterId, TestSeasonId);
        var character = await charGrain.GetCharacterAsync();
        Assert.True(character.Restrictions.HasFlag(CharacterRestrictions.Hardcore));
        Assert.True(character.Restrictions.HasFlag(CharacterRestrictions.SoloSelfFound));
    }
}
