using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for account operations.
/// Replaces AccountController with bidirectional communication.
/// </summary>
public class AccountHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public AccountHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get account info including unlocked cosmetics and achievements.
    /// </summary>
    public async Task<Account> GetAccount(Guid accountId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        return await grain.GetAccountAsync();
    }

    /// <summary>
    /// Get all characters for an account across all seasons.
    /// </summary>
    public async Task<IReadOnlyList<CharacterSummary>> GetCharacters(Guid accountId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        return await grain.GetCharactersAsync();
    }

    /// <summary>
    /// Create a new character in a specific season with chosen restrictions.
    /// </summary>
    public async Task<CharacterSummary> CreateCharacter(Guid accountId, string seasonId, string name, CharacterRestrictions restrictions = CharacterRestrictions.None)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        return await grain.CreateCharacterAsync(seasonId, name, restrictions);
    }

    /// <summary>
    /// Check if a cosmetic is unlocked.
    /// </summary>
    public async Task<bool> HasCosmetic(Guid accountId, string cosmeticId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        return await grain.HasCosmeticAsync(cosmeticId);
    }

    /// <summary>
    /// Unlock a cosmetic for an account.
    /// </summary>
    public async Task UnlockCosmetic(Guid accountId, string cosmeticId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        await grain.UnlockCosmeticAsync(cosmeticId);
    }

    /// <summary>
    /// Check if an achievement is unlocked.
    /// </summary>
    public async Task<bool> HasAchievement(Guid accountId, string achievementId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        return await grain.HasAchievementAsync(achievementId);
    }

    /// <summary>
    /// Unlock an achievement for an account.
    /// </summary>
    public async Task UnlockAchievement(Guid accountId, string achievementId)
    {
        var grain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        await grain.UnlockAchievementAsync(achievementId);
    }
}
