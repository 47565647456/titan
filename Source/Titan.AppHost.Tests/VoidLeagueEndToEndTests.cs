using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// End-to-end integration tests for Void League functionality.
/// Tests the full stack via SignalR hubs.
/// </summary>
[Collection("AppHost")]
public class VoidLeagueEndToEndTests : IntegrationTestBase
{
    public VoidLeagueEndToEndTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task VoidLeague_CanBeCreatedViaHub()
    {
        // Arrange
        await using var admin = await CreateAdminSessionAsync();
        var seasonHub = await admin.GetSeasonHubAsync();
        var voidSeasonId = $"void-e2e-{Guid.NewGuid():N}";

        // Act - Create void league via hub
        var season = await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            voidSeasonId,
            "Void E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            true); // isVoid = true

        // Assert
        Assert.NotNull(season);
        Assert.Equal(voidSeasonId, season.SeasonId);
        Assert.True(season.IsVoid);
        Assert.Equal(SeasonType.Temporary, season.Type);
    }

    [Fact]
    public async Task VoidLeague_HardcoreDeathDoesNotMigrate()
    {
        // Arrange - Create void league and character
        await using var admin = await CreateAdminSessionAsync();
        await using var user = await CreateUserSessionAsync();

        var seasonHub = await admin.GetSeasonHubAsync();
        var voidSeasonId = $"void-hc-e2e-{Guid.NewGuid():N}";

        // Create void season
        await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            voidSeasonId,
            "Void HC E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            true); // isVoid = true

        // Create hardcore character in void league
        var accountHub = await user.GetAccountHubAsync();
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", voidSeasonId, "VoidHCPlayer", CharacterRestrictions.Hardcore);

        // Act - Kill the character via CharacterHub
        var characterHub = await user.GetCharacterHubAsync();
        var dieResult = await characterHub.InvokeAsync<DieResult>(
            "Die", charSummary.CharacterId, voidSeasonId);

        // Assert - Character is dead but NOT migrated
        Assert.True(dieResult.Character.IsDead);
        Assert.False(dieResult.Migrated);
    }

    [Fact]
    public async Task VoidLeague_MigrationAttempt_ShouldFail()
    {
        // Arrange - Create void league and end it
        await using var admin = await CreateAdminSessionAsync();
        var seasonHub = await admin.GetSeasonHubAsync();
        var voidSeasonId = $"void-migrate-e2e-{Guid.NewGuid():N}";

        // Create void season
        await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            voidSeasonId,
            "Void Migration E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            true); // isVoid = true

        // End the season
        await seasonHub.InvokeAsync<Season>("EndSeason", voidSeasonId);

        // Act & Assert - Attempting to start migration should throw
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            seasonHub.InvokeAsync<MigrationStatus>("StartMigration", voidSeasonId, "standard"));

        Assert.Contains("Void League", ex.Message);
    }

    [Fact]
    public async Task NonVoidLeague_MigrationSucceeds()
    {
        // Arrange - Create a normal (non-void) league
        await using var admin = await CreateAdminSessionAsync();
        await using var user = await CreateUserSessionAsync();

        var seasonHub = await admin.GetSeasonHubAsync();
        var normalSeasonId = $"normal-e2e-{Guid.NewGuid():N}";

        // Create normal season (isVoid = false)
        await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            normalSeasonId,
            "Normal E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            false); // isVoid = false

        // Create a character
        var accountHub = await user.GetAccountHubAsync();
        await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", normalSeasonId, "NormalLeaguePlayer", CharacterRestrictions.None);

        // End the season
        await seasonHub.InvokeAsync<Season>("EndSeason", normalSeasonId);

        // Act - Start migration (should succeed for non-void leagues)
        var migrationStatus = await seasonHub.InvokeAsync<MigrationStatus>(
            "StartMigration", normalSeasonId, "standard");

        // Assert
        Assert.Equal(MigrationState.InProgress, migrationStatus.State);
        Assert.Equal(normalSeasonId, migrationStatus.SourceSeasonId);
        Assert.Equal("standard", migrationStatus.TargetSeasonId);
    }

    [Fact]
    public async Task VoidLeague_GetAllSeasons_IncludesIsVoidFlag()
    {
        // Arrange - Create a void league
        await using var admin = await CreateAdminSessionAsync();
        var seasonHub = await admin.GetSeasonHubAsync();
        var voidSeasonId = $"void-list-e2e-{Guid.NewGuid():N}";

        await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            voidSeasonId,
            "Void List E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            true);

        // Act - Get all seasons
        var seasons = await seasonHub.InvokeAsync<IReadOnlyList<Season>>("GetAllSeasons");

        // Assert - Find our void season and verify IsVoid flag
        var voidSeason = seasons.FirstOrDefault(s => s.SeasonId == voidSeasonId);
        Assert.NotNull(voidSeason);
        Assert.True(voidSeason!.IsVoid);
    }

    [Fact]
    public async Task NonVoidLeague_HardcoreDeathMigrates_E2E()
    {
        // Arrange - Create normal league and HC character
        await using var admin = await CreateAdminSessionAsync();
        await using var user = await CreateUserSessionAsync();

        var seasonHub = await admin.GetSeasonHubAsync();
        var normalSeasonId = $"normal-hc-e2e-{Guid.NewGuid():N}";

        // Create normal season (isVoid = false)
        await seasonHub.InvokeAsync<Season>(
            "CreateSeason",
            normalSeasonId,
            "Normal HC E2E League",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            (DateTimeOffset?)null,
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            false); // isVoid = false

        // Create hardcore character
        var accountHub = await user.GetAccountHubAsync();
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", normalSeasonId, "NormalHCPlayer", CharacterRestrictions.Hardcore);

        // Act - Kill the character
        var characterHub = await user.GetCharacterHubAsync();
        var dieResult = await characterHub.InvokeAsync<DieResult>(
            "Die", charSummary.CharacterId, normalSeasonId);

        // Assert - Character IS migrated to standard
        Assert.True(dieResult.Character.IsDead);
        Assert.True(dieResult.Migrated);

        // Verify character exists in standard (via GetCharacter)
        var standardChar = await characterHub.InvokeAsync<Character>(
            "GetCharacter", charSummary.CharacterId, "standard");
        Assert.Equal("NormalHCPlayer", standardChar.Name);
        Assert.False(standardChar.Restrictions.HasFlag(CharacterRestrictions.Hardcore));
    }

    [Fact]
    public async Task CharacterHistory_CanBeRetrievedViaHub()
    {
        // Arrange - Create a character
        await using var user = await CreateUserSessionAsync();

        var accountHub = await user.GetAccountHubAsync();
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "HistoryE2ETest", CharacterRestrictions.None);

        // Act - Get character history
        var characterHub = await user.GetCharacterHubAsync();
        var history = await characterHub.InvokeAsync<IReadOnlyList<CharacterHistoryEntry>>(
            "GetHistory", charSummary.CharacterId, "standard");

        // Assert - History contains Created event
        Assert.NotEmpty(history);
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.Created);
    }
}

/// <summary>
/// Result from CharacterHub.Die matching the API contract.
/// </summary>
public record DieResult(Character Character, bool Migrated);
