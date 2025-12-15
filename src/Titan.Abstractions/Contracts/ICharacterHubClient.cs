using Titan.Abstractions.Models;

namespace Titan.Abstractions.Contracts;

/// <summary>
/// Strongly-typed client contract for CharacterHub operations.
/// Used with TypedSignalR.Client source generator.
/// </summary>
public interface ICharacterHubClient
{
    /// <summary>
    /// Get character details (verifies ownership).
    /// </summary>
    Task<Character> GetCharacter(Guid characterId, string seasonId);

    /// <summary>
    /// Add experience to a character (verifies ownership).
    /// </summary>
    Task<Character> AddExperience(Guid characterId, string seasonId, long amount);

    /// <summary>
    /// Set a character stat (verifies ownership).
    /// </summary>
    Task<Character> SetStat(Guid characterId, string seasonId, string statName, int value);

    /// <summary>
    /// Get challenge progress for a character (verifies ownership).
    /// </summary>
    Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgress(Guid characterId, string seasonId);

    /// <summary>
    /// Update challenge progress for a character (verifies ownership).
    /// </summary>
    Task UpdateChallengeProgress(Guid characterId, string seasonId, string challengeId, int progress);

    /// <summary>
    /// Get character history (verifies ownership).
    /// </summary>
    Task<IReadOnlyList<CharacterHistoryEntry>> GetHistory(Guid characterId, string seasonId);
}
