using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for AccountHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface IAccountHubClient
{
    /// <summary>
    /// Get the authenticated user's account info including unlocked cosmetics and achievements.
    /// </summary>
    Task<Account> GetAccount();

    /// <summary>
    /// Get all characters for the authenticated user's account across all seasons.
    /// </summary>
    Task<IReadOnlyList<CharacterSummary>> GetCharacters();

    /// <summary>
    /// Create a new character for the authenticated user in a specific season with chosen restrictions.
    /// </summary>
    Task<CharacterSummary> CreateCharacter(string seasonId, string name, CharacterRestrictions restrictions);

    /// <summary>
    /// Check if a cosmetic is unlocked for the authenticated user.
    /// </summary>
    Task<bool> HasCosmetic(string cosmeticId);

    /// <summary>
    /// Unlock a cosmetic for the authenticated user's account.
    /// </summary>
    Task UnlockCosmetic(string cosmeticId);

    /// <summary>
    /// Check if an achievement is unlocked for the authenticated user.
    /// </summary>
    Task<bool> HasAchievement(string achievementId);

    /// <summary>
    /// Unlock an achievement for the authenticated user's account.
    /// </summary>
    Task UnlockAchievement(string achievementId);
}
