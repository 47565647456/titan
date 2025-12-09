using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SeasonController : ControllerBase
{
    private readonly IClusterClient _clusterClient;

    public SeasonController(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get all seasons (permanent and temporary).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllSeasons()
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        var seasons = await registry.GetAllSeasonsAsync();
        return Ok(seasons);
    }

    /// <summary>
    /// Get the currently active temporary season.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentSeason()
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.GetCurrentSeasonAsync();
        
        if (season == null)
            return NotFound(new { Message = "No active temporary season." });
        
        return Ok(season);
    }

    /// <summary>
    /// Get a specific season by ID.
    /// </summary>
    [HttpGet("{seasonId}")]
    public async Task<IActionResult> GetSeason(string seasonId)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.GetSeasonAsync(seasonId);
        
        if (season == null)
            return NotFound(new { Message = $"Season '{seasonId}' not found." });
        
        return Ok(season);
    }

    /// <summary>
    /// Create a new season (Admin only).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateSeason([FromBody] CreateSeasonRequest request)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        
        try
        {
            var season = new Season
            {
                SeasonId = request.SeasonId,
                Name = request.Name,
                Type = request.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Status = request.Status,
                MigrationTargetId = request.MigrationTargetId ?? "standard",
                Modifiers = request.Modifiers
            };
            
            var created = await registry.CreateSeasonAsync(season);
            return CreatedAtAction(nameof(GetSeason), new { seasonId = created.SeasonId }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// End a season and trigger migration (Admin only).
    /// </summary>
    [HttpPost("{seasonId}/end")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EndSeason(string seasonId)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        
        try
        {
            var season = await registry.EndSeasonAsync(seasonId);
            return Ok(new { 
                Message = $"Season '{seasonId}' ended. Migration to '{season.MigrationTargetId}' will begin.",
                Season = season
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Update a season's status (Admin only).
    /// </summary>
    [HttpPatch("{seasonId}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSeasonStatus(string seasonId, [FromBody] UpdateSeasonStatusRequest request)
    {
        var registry = _clusterClient.GetGrain<ISeasonRegistryGrain>("default");
        
        try
        {
            var season = await registry.UpdateSeasonStatusAsync(seasonId, request.Status);
            return Ok(season);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get migration status for a season.
    /// </summary>
    [HttpGet("{seasonId}/migration")]
    public async Task<IActionResult> GetMigrationStatus(string seasonId)
    {
        var migrationGrain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        var status = await migrationGrain.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// Start migration for a season (Admin only).
    /// </summary>
    [HttpPost("{seasonId}/migration/start")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> StartMigration(string seasonId, [FromBody] StartMigrationRequest? request = null)
    {
        var migrationGrain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        
        try
        {
            var targetSeasonId = request?.TargetSeasonId ?? "standard";
            var status = await migrationGrain.StartMigrationAsync(targetSeasonId);
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Migrate a specific character (Admin only).
    /// </summary>
    [HttpPost("{seasonId}/migration/character/{characterId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MigrateCharacter(string seasonId, Guid characterId)
    {
        var migrationGrain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        
        try
        {
            var status = await migrationGrain.MigrateCharacterAsync(characterId);
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Cancel an in-progress migration (Admin only).
    /// </summary>
    [HttpPost("{seasonId}/migration/cancel")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CancelMigration(string seasonId)
    {
        var migrationGrain = _clusterClient.GetGrain<ISeasonMigrationGrain>(seasonId);
        
        try
        {
            await migrationGrain.CancelMigrationAsync();
            var status = await migrationGrain.GetStatusAsync();
            return Ok(status);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

public record CreateSeasonRequest(
    string SeasonId,
    string Name,
    SeasonType Type,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate = null,
    SeasonStatus Status = SeasonStatus.Upcoming,
    string? MigrationTargetId = null,
    Dictionary<string, object>? Modifiers = null
);

public record UpdateSeasonStatusRequest(SeasonStatus Status);
public record StartMigrationRequest(string TargetSeasonId);
