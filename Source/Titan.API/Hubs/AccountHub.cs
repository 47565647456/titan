using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for account operations.
/// All operations are scoped to the authenticated user's account.
/// </summary>
[Authorize]
public class AccountHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public AccountHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Gets the authenticated user's ID from the JWT token.
    /// </summary>
    private Guid GetUserId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Get the authenticated user's account info including unlocked cosmetics and achievements.
    /// </summary>
    public async Task<Account> GetAccount()
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        return await grain.GetAccountAsync();
    }

    /// <summary>
    /// Get all characters for the authenticated user's account across all seasons.
    /// </summary>
    public async Task<IReadOnlyList<CharacterSummary>> GetCharacters()
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        return await grain.GetCharactersAsync();
    }

    /// <summary>
    /// Create a new character for the authenticated user in a specific season with chosen restrictions.
    /// </summary>
    public async Task<CharacterSummary> CreateCharacter(string seasonId, string name, CharacterRestrictions restrictions = CharacterRestrictions.None)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        return await grain.CreateCharacterAsync(seasonId, name, restrictions);
    }

    /// <summary>
    /// Check if a cosmetic is unlocked for the authenticated user.
    /// </summary>
    public async Task<bool> HasCosmetic(string cosmeticId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        return await grain.HasCosmeticAsync(cosmeticId);
    }

    /// <summary>
    /// Unlock a cosmetic for the authenticated user's account.
    /// </summary>
    public async Task UnlockCosmetic(string cosmeticId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        await grain.UnlockCosmeticAsync(cosmeticId);
    }

    /// <summary>
    /// Check if an achievement is unlocked for the authenticated user.
    /// </summary>
    public async Task<bool> HasAchievement(string achievementId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        return await grain.HasAchievementAsync(achievementId);
    }

    /// <summary>
    /// Unlock an achievement for the authenticated user's account.
    /// </summary>
    public async Task UnlockAchievement(string achievementId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        await grain.UnlockAchievementAsync(achievementId);
    }
}

