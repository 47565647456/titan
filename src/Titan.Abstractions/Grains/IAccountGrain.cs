using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Global account grain - persists across all seasons.
/// Key: AccountId (Guid)
/// </summary>
public interface IAccountGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Gets the account info.
    /// </summary>
    Task<Account> GetAccountAsync();

    /// <summary>
    /// Gets all characters for this account across all seasons.
    /// </summary>
    Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync();

    /// <summary>
    /// Creates a new character in the specified season with chosen restrictions.
    /// </summary>
    Task<CharacterSummary> CreateCharacterAsync(string seasonId, string name, CharacterRestrictions restrictions);

    /// <summary>
    /// Updates a character summary (called by CharacterGrain on changes).
    /// </summary>
    Task UpdateCharacterSummaryAsync(CharacterSummary summary);

    /// <summary>
    /// Unlocks a cosmetic globally for this account.
    /// </summary>
    Task UnlockCosmeticAsync(string cosmeticId);

    /// <summary>
    /// Unlocks an achievement globally for this account.
    /// </summary>
    Task UnlockAchievementAsync(string achievementId);

    /// <summary>
    /// Checks if a cosmetic is unlocked.
    /// </summary>
    Task<bool> HasCosmeticAsync(string cosmeticId);

    /// <summary>
    /// Checks if an achievement is unlocked.
    /// </summary>
    Task<bool> HasAchievementAsync(string achievementId);
}
