using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for character operations.
/// All operations verify the character belongs to the authenticated user.
/// </summary>
[Authorize]
public class CharacterHub : Hub
{
    private readonly IClusterClient _clusterClient;

    public CharacterHub(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Gets the authenticated user's ID from the JWT token.
    /// </summary>
    private Guid GetUserId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Verifies that the specified character belongs to the authenticated user.
    /// </summary>
    private async Task VerifyCharacterOwnershipAsync(Guid characterId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(GetUserId());
        var characters = await accountGrain.GetCharactersAsync();
        
        if (!characters.Any(c => c.CharacterId == characterId))
        {
            throw new HubException("Character does not belong to this account.");
        }
    }

    /// <summary>
    /// Get character details (verifies ownership).
    /// </summary>
    public async Task<Character> GetCharacter(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetCharacterAsync();
    }

    /// <summary>
    /// Add experience to a character (verifies ownership).
    /// </summary>
    public async Task<Character> AddExperience(Guid characterId, string seasonId, long amount)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.AddExperienceAsync(amount);
    }

    /// <summary>
    /// Set a character stat (verifies ownership).
    /// </summary>
    public async Task<Character> SetStat(Guid characterId, string seasonId, string statName, int value)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.SetStatAsync(statName, value);
    }

    /// <summary>
    /// Get challenge progress for a character (verifies ownership).
    /// </summary>
    public async Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgress(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        return await grain.GetChallengeProgressAsync();
    }

    /// <summary>
    /// Update challenge progress for a character (verifies ownership).
    /// </summary>
    public async Task UpdateChallengeProgress(Guid characterId, string seasonId, string challengeId, int progress)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        await grain.UpdateChallengeProgressAsync(challengeId, progress);
    }

    /// <summary>
    /// Kill a Hardcore character (verifies ownership, triggers migration to permanent league).
    /// </summary>
    public async Task<DieResult> Die(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        
        var grain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        var character = await grain.DieAsync();
        return new DieResult(character, character.IsMigrated);
    }
}

public record DieResult(Character Character, bool Migrated);

