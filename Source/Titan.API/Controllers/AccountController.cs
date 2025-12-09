using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly IClusterClient _clusterClient;

    public AccountController(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get account info including unlocked cosmetics and achievements.
    /// </summary>
    [HttpGet("{accountId:guid}")]
    public async Task<IActionResult> GetAccount(Guid accountId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        var account = await accountGrain.GetAccountAsync();
        return Ok(account);
    }

    /// <summary>
    /// Get all characters for an account across all seasons.
    /// </summary>
    [HttpGet("{accountId:guid}/characters")]
    public async Task<IActionResult> GetCharacters(Guid accountId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        var characters = await accountGrain.GetCharactersAsync();
        return Ok(characters);
    }

    /// <summary>
    /// Create a new character in a specific season with chosen restrictions.
    /// </summary>
    [HttpPost("{accountId:guid}/characters")]
    public async Task<IActionResult> CreateCharacter(Guid accountId, [FromBody] CreateCharacterRequest request)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        
        try
        {
            var character = await accountGrain.CreateCharacterAsync(
                request.SeasonId, 
                request.Name, 
                request.Restrictions);
            
            return CreatedAtAction(
                nameof(CharacterController.GetCharacter), 
                "Character",
                new { characterId = character.CharacterId, seasonId = character.SeasonId }, 
                character);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Check if a cosmetic is unlocked.
    /// </summary>
    [HttpGet("{accountId:guid}/cosmetics/{cosmeticId}")]
    public async Task<IActionResult> HasCosmetic(Guid accountId, string cosmeticId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        var hasCosmetic = await accountGrain.HasCosmeticAsync(cosmeticId);
        return Ok(new { CosmeticId = cosmeticId, Unlocked = hasCosmetic });
    }

    /// <summary>
    /// Unlock a cosmetic for an account.
    /// </summary>
    [HttpPost("{accountId:guid}/cosmetics/{cosmeticId}")]
    public async Task<IActionResult> UnlockCosmetic(Guid accountId, string cosmeticId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        await accountGrain.UnlockCosmeticAsync(cosmeticId);
        return Ok(new { CosmeticId = cosmeticId, Unlocked = true });
    }

    /// <summary>
    /// Check if an achievement is unlocked.
    /// </summary>
    [HttpGet("{accountId:guid}/achievements/{achievementId}")]
    public async Task<IActionResult> HasAchievement(Guid accountId, string achievementId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        var hasAchievement = await accountGrain.HasAchievementAsync(achievementId);
        return Ok(new { AchievementId = achievementId, Unlocked = hasAchievement });
    }

    /// <summary>
    /// Unlock an achievement for an account.
    /// </summary>
    [HttpPost("{accountId:guid}/achievements/{achievementId}")]
    public async Task<IActionResult> UnlockAchievement(Guid accountId, string achievementId)
    {
        var accountGrain = _clusterClient.GetGrain<IAccountGrain>(accountId);
        await accountGrain.UnlockAchievementAsync(achievementId);
        return Ok(new { AchievementId = achievementId, Unlocked = true });
    }
}

public record CreateCharacterRequest(
    string SeasonId,
    string Name,
    CharacterRestrictions Restrictions = CharacterRestrictions.None
);
