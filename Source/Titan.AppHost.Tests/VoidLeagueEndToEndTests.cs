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
        var (adminToken, _) = await LoginAsAdminAsync();
        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
        var voidSeasonId = $"void-e2e-{Guid.NewGuid():N}";

        try
        {
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
        finally
        {
            await seasonHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task VoidLeague_HardcoreDeathDoesNotMigrate()
    {
        // Arrange - Create void league and character
        var (adminToken, _) = await LoginAsAdminAsync();
        var (userToken, userId) = await LoginAsUserAsync();

        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
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

        await seasonHub.DisposeAsync();

        // Create hardcore character in void league
        var accountHub = await ConnectToHubAsync("/accountHub", userToken);
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", voidSeasonId, "VoidHCPlayer", CharacterRestrictions.Hardcore);
        await accountHub.DisposeAsync();

        // Act - Kill the character via CharacterHub
        var characterHub = await ConnectToHubAsync("/characterHub", userToken);
        var dieResult = await characterHub.InvokeAsync<DieResult>(
            "Die", charSummary.CharacterId, voidSeasonId);

        // Assert - Character is dead but NOT migrated
        Assert.True(dieResult.Character.IsDead);
        Assert.False(dieResult.Migrated);

        await characterHub.DisposeAsync();
    }

    [Fact]
    public async Task VoidLeague_MigrationAttempt_ShouldFail()
    {
        // Arrange - Create void league and end it
        var (adminToken, _) = await LoginAsAdminAsync();
        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
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

        await seasonHub.DisposeAsync();
    }

    [Fact]
    public async Task NonVoidLeague_MigrationSucceeds()
    {
        // Arrange - Create a normal (non-void) league
        var (adminToken, _) = await LoginAsAdminAsync();
        var (userToken, userId) = await LoginAsUserAsync();

        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
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
        var accountHub = await ConnectToHubAsync("/accountHub", userToken);
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", normalSeasonId, "NormalLeaguePlayer", CharacterRestrictions.None);
        await accountHub.DisposeAsync();

        // End the season
        await seasonHub.InvokeAsync<Season>("EndSeason", normalSeasonId);

        // Act - Start migration (should succeed for non-void leagues)
        var migrationStatus = await seasonHub.InvokeAsync<MigrationStatus>(
            "StartMigration", normalSeasonId, "standard");

        // Assert
        Assert.Equal(MigrationState.InProgress, migrationStatus.State);
        Assert.Equal(normalSeasonId, migrationStatus.SourceSeasonId);
        Assert.Equal("standard", migrationStatus.TargetSeasonId);

        await seasonHub.DisposeAsync();
    }

    [Fact]
    public async Task VoidLeague_GetAllSeasons_IncludesIsVoidFlag()
    {
        // Arrange - Create a void league
        var (adminToken, _) = await LoginAsAdminAsync();
        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
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

        await seasonHub.DisposeAsync();
    }

    [Fact]
    public async Task NonVoidLeague_HardcoreDeathMigrates_E2E()
    {
        // Arrange - Create normal league and HC character
        var (adminToken, _) = await LoginAsAdminAsync();
        var (userToken, userId) = await LoginAsUserAsync();

        var seasonHub = await ConnectToHubAsync("/seasonHub", adminToken);
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

        await seasonHub.DisposeAsync();

        // Create hardcore character
        var accountHub = await ConnectToHubAsync("/accountHub", userToken);
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", normalSeasonId, "NormalHCPlayer", CharacterRestrictions.Hardcore);
        await accountHub.DisposeAsync();

        // Act - Kill the character
        var characterHub = await ConnectToHubAsync("/characterHub", userToken);
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

        await characterHub.DisposeAsync();
    }

    [Fact]
    public async Task CharacterHistory_CanBeRetrievedViaHub()
    {
        // Arrange - Create a character
        var (userToken, userId) = await LoginAsUserAsync();

        var accountHub = await ConnectToHubAsync("/accountHub", userToken);
        var charSummary = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "HistoryE2ETest", CharacterRestrictions.None);
        await accountHub.DisposeAsync();

        // Act - Get character history
        var characterHub = await ConnectToHubAsync("/characterHub", userToken);
        var history = await characterHub.InvokeAsync<IReadOnlyList<CharacterHistoryEntry>>(
            "GetHistory", charSummary.CharacterId, "standard");

        // Assert - History contains Created event
        Assert.NotEmpty(history);
        Assert.Contains(history, h => h.EventType == CharacterEventTypes.Created);

        await characterHub.DisposeAsync();
    }
}

/// <summary>
/// Result from CharacterHub.Die matching the API contract.
/// </summary>
public record DieResult(Character Character, bool Migrated);
