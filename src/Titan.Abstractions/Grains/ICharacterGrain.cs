using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Per-season character grain with compound key.
/// Key: (CharacterId, SeasonId)
/// </summary>
public interface ICharacterGrain : IGrainWithGuidCompoundKey
{
    /// <summary>
    /// Gets the full character data.
    /// </summary>
    Task<Character> GetCharacterAsync();

    /// <summary>
    /// Initializes a new character (called during creation).
    /// </summary>
    Task<Character> InitializeAsync(Guid accountId, string name, CharacterRestrictions restrictions);

    /// <summary>
    /// Adds experience to the character.
    /// </summary>
    Task<Character> AddExperienceAsync(long amount);

    /// <summary>
    /// Sets a stat value.
    /// </summary>
    Task<Character> SetStatAsync(string statName, int value);

    /// <summary>
    /// Gets all challenge progress for this character.
    /// </summary>
    Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgressAsync();

    /// <summary>
    /// Updates progress on a challenge.
    /// </summary>
    Task UpdateChallengeProgressAsync(string challengeId, int progress);

    /// <summary>
    /// Called when a Hardcore character dies.
    /// Marks the character as dead and triggers migration to the permanent season.
    /// </summary>
    Task<Character> DieAsync();

    /// <summary>
    /// Migrates this character to a new season (e.g., at season end or on death).
    /// Creates a copy in the target season and marks this one as migrated.
    /// </summary>
    Task<Character> MigrateToSeasonAsync(string targetSeasonId);

    /// <summary>
    /// Gets the character's event history (created, died, migrated, etc.).
    /// Events are returned in chronological order.
    /// </summary>
    Task<IReadOnlyList<CharacterHistoryEntry>> GetHistoryAsync();

    /// <summary>
    /// Adds a custom event to the character's history.
    /// Use CharacterEventTypes for standard events, or any custom string.
    /// </summary>
    Task AddHistoryEntryAsync(string eventType, string description, Dictionary<string, string>? data = null);
}
