using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CharacterController : ControllerBase
{
    private readonly IClusterClient _clusterClient;

    public CharacterController(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get character details.
    /// </summary>
    [HttpGet("{characterId:guid}/{seasonId}")]
    public async Task<IActionResult> GetCharacter(Guid characterId, string seasonId)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            var character = await characterGrain.GetCharacterAsync();
            return Ok(character);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { Message = "Character not found." });
        }
    }

    /// <summary>
    /// Add experience to a character.
    /// </summary>
    [HttpPost("{characterId:guid}/{seasonId}/experience")]
    public async Task<IActionResult> AddExperience(Guid characterId, string seasonId, [FromBody] AddExperienceRequest request)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            var character = await characterGrain.AddExperienceAsync(request.Amount);
            return Ok(character);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Set a character stat.
    /// </summary>
    [HttpPut("{characterId:guid}/{seasonId}/stats/{statName}")]
    public async Task<IActionResult> SetStat(Guid characterId, string seasonId, string statName, [FromBody] SetStatRequest request)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            var character = await characterGrain.SetStatAsync(statName, request.Value);
            return Ok(character);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get challenge progress for a character.
    /// </summary>
    [HttpGet("{characterId:guid}/{seasonId}/challenges")]
    public async Task<IActionResult> GetChallengeProgress(Guid characterId, string seasonId)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            var progress = await characterGrain.GetChallengeProgressAsync();
            return Ok(progress);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Update challenge progress for a character.
    /// </summary>
    [HttpPost("{characterId:guid}/{seasonId}/challenges/{challengeId}")]
    public async Task<IActionResult> UpdateChallengeProgress(
        Guid characterId, 
        string seasonId, 
        string challengeId, 
        [FromBody] UpdateChallengeProgressRequest request)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            await characterGrain.UpdateChallengeProgressAsync(challengeId, request.Progress);
            var allProgress = await characterGrain.GetChallengeProgressAsync();
            return Ok(allProgress);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Kill a Hardcore character (triggers migration to permanent league).
    /// </summary>
    [HttpPost("{characterId:guid}/{seasonId}/die")]
    public async Task<IActionResult> Die(Guid characterId, string seasonId)
    {
        var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId, seasonId);
        
        try
        {
            var character = await characterGrain.DieAsync();
            return Ok(new { 
                Message = "Character died.",
                Character = character,
                Migrated = character.IsMigrated
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

public record AddExperienceRequest(long Amount);
public record SetStatRequest(int Value);
public record UpdateChallengeProgressRequest(int Progress);
