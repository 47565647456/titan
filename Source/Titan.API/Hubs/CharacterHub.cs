using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for character operations.
/// Replaces CharacterController with bidirectional communication.
/// </summary>
public class CharacterHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public CharacterHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get character details.
    /// </summary>
    public async Task<Character> GetCharacter(Guid characterId, string seasonId)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetCharacterAsync();
    }

    /// <summary>
    /// Add experience to a character.
    /// </summary>
    public async Task<Character> AddExperience(Guid characterId, string seasonId, long amount)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.AddExperienceAsync(amount);
    }

    /// <summary>
    /// Set a character stat.
    /// </summary>
    public async Task<Character> SetStat(Guid characterId, string seasonId, string statName, int value)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.SetStatAsync(statName, value);
    }

    /// <summary>
    /// Get challenge progress for a character.
    /// </summary>
    public async Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgress(Guid characterId, string seasonId)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetChallengeProgressAsync();
    }

    /// <summary>
    /// Update challenge progress for a character.
    /// </summary>
    public async Task UpdateChallengeProgress(Guid characterId, string seasonId, string challengeId, int progress)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        await grain.UpdateChallengeProgressAsync(challengeId, progress);
    }

    /// <summary>
    /// Kill a Hardcore character (triggers migration to permanent league).
    /// </summary>
    public async Task<DieResult> Die(Guid characterId, string seasonId)
    {
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        var character = await grain.DieAsync();
        return new DieResult(character, character.IsMigrated);
    }
}

public record DieResult(Character Character, bool Migrated);
