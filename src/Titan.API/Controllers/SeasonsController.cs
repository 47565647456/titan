using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Controllers;

/// <summary>
/// Season management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/seasons")]
[Tags("Admin - Seasons")]
[Authorize(Policy = "AdminDashboard")]
public class SeasonsController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<SeasonsController> _logger;

    public SeasonsController(
        IClusterClient clusterClient,
        ILogger<SeasonsController> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    private ISeasonRegistryGrain GetGrain() => _clusterClient.GetGrain<ISeasonRegistryGrain>("default");

    /// <summary>
    /// Get all seasons.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Season>>> GetAll()
    {
        var seasons = await GetGrain().GetAllSeasonsAsync();
        return Ok(seasons);
    }

    /// <summary>
    /// Get season by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Season>> GetById(string id)
    {
        var season = await GetGrain().GetSeasonAsync(id);
        if (season == null)
        {
            return NotFound();
        }
        return Ok(season);
    }

    /// <summary>
    /// Create a new season.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Season>> Create([FromBody] CreateSeasonRequest request)
    {
        var season = new Season
        {
            SeasonId = request.SeasonId,
            Name = request.Name,
            Type = request.Type,
            Status = request.Status,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            MigrationTargetId = request.MigrationTargetId,
            IsVoid = request.IsVoid
        };

        await GetGrain().CreateSeasonAsync(season);
        _logger.LogInformation("Created season {SeasonId}", season.SeasonId);

        return CreatedAtAction(nameof(GetById), new { id = season.SeasonId }, season);
    }

    /// <summary>
    /// Update season status.
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateSeasonStatusRequest request)
    {
        await GetGrain().UpdateSeasonStatusAsync(id, request.Status);
        _logger.LogInformation("Updated season {SeasonId} status to {Status}", id, request.Status);
        return Ok(new { success = true });
    }

    /// <summary>
    /// End a season (triggers character migration).
    /// </summary>
    [HttpPost("{id}/end")]
    public async Task<IActionResult> EndSeason(string id)
    {
        await GetGrain().EndSeasonAsync(id);
        _logger.LogInformation("Ended season {SeasonId}", id);
        return Ok(new { success = true });
    }
}

// DTOs

public record CreateSeasonRequest
{
    public required string SeasonId { get; init; }
    public required string Name { get; init; }
    public SeasonType Type { get; init; } = SeasonType.Temporary;
    public SeasonStatus Status { get; init; } = SeasonStatus.Upcoming;
    public DateTimeOffset StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string MigrationTargetId { get; init; } = "standard";
    public bool IsVoid { get; init; }
}

public record UpdateSeasonStatusRequest
{
    public SeasonStatus Status { get; init; }
}
