using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for character operations.
/// All operations verify the character belongs to the authenticated user.
/// </summary>
[Authorize]
public class CharacterHub : TitanHubBase
{
    private readonly HubValidationService _validation;

    public CharacterHub(IClusterClient clusterClient, HubValidationService validation, ILogger<CharacterHub> logger)
        : base(clusterClient, logger)
    {
        _validation = validation;
    }

    // VerifyCharacterOwnershipAsync is inherited from TitanHubBase

    /// <summary>
    /// Get character details (verifies ownership).
    /// </summary>
    public async Task<Character> GetCharacter(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetCharacterAsync();
    }

    /// <summary>
    /// Add experience to a character (verifies ownership).
    /// </summary>
    public async Task<Character> AddExperience(Guid characterId, string seasonId, long amount)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        await _validation.ValidatePositiveAsync(amount, nameof(amount));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.AddExperienceAsync(amount);
    }

    /// <summary>
    /// Set a character stat (verifies ownership).
    /// </summary>
    public async Task<Character> SetStat(Guid characterId, string seasonId, string statName, int value)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        await _validation.ValidateNameAsync(statName, nameof(statName), 100);
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.SetStatAsync(statName, value);
    }

    /// <summary>
    /// Get challenge progress for a character (verifies ownership).
    /// </summary>
    public async Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgress(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetChallengeProgressAsync();
    }

    /// <summary>
    /// Update challenge progress for a character (verifies ownership).
    /// </summary>
    public async Task UpdateChallengeProgress(Guid characterId, string seasonId, string challengeId, int progress)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        await _validation.ValidateIdAsync(challengeId, nameof(challengeId));
        await _validation.ValidatePositiveAsync(progress, nameof(progress));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        await grain.UpdateChallengeProgressAsync(challengeId, progress);
    }

    /// <summary>
    /// Kill a Hardcore character (verifies ownership, triggers migration to permanent league).
    /// </summary>
    public async Task<DieResult> Die(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        var character = await grain.DieAsync();
        return new DieResult(character, character.IsMigrated);
    }

    /// <summary>
    /// Get character history (verifies ownership).
    /// Returns chronological list of significant events (created, died, migrated, etc.).
    /// </summary>
    public async Task<IReadOnlyList<CharacterHistoryEntry>> GetHistory(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        await _validation.ValidateIdAsync(seasonId, nameof(seasonId));
        
        var grain = ClusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetHistoryAsync();
    }
}

public record DieResult(Character Character, bool Migrated);
