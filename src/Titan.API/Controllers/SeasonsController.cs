using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.Abstractions.RateLimiting;

namespace Titan.API.Controllers;

/// <summary>
/// Season management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/seasons")]
[Tags("Admin - Seasons")]
[Authorize(Policy = "AdminDashboard")]
[RateLimitPolicy("Admin")]
public class SeasonsController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<SeasonsController> _logger;
    private readonly IValidator<CreateSeasonRequest> _createValidator;
    private readonly IValidator<UpdateSeasonStatusRequest> _statusValidator;

    public SeasonsController(
        IClusterClient clusterClient,
        ILogger<SeasonsController> logger,
        IValidator<CreateSeasonRequest> createValidator,
        IValidator<UpdateSeasonStatusRequest> statusValidator)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _createValidator = createValidator;
        _statusValidator = statusValidator;
    }

    private ISeasonRegistryGrain GetGrain() => _clusterClient.GetGrain<ISeasonRegistryGrain>("default");

    /// <summary>
    /// Get all seasons.
    /// </summary>
    /// <returns>List of all seasons (permanent and temporary).</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<Season>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Season>>> GetAll()
    {
        var seasons = await GetGrain().GetAllSeasonsAsync();
        return Ok(seasons);
    }

    /// <summary>
    /// Get season by ID.
    /// </summary>
    /// <param name="id">The unique season identifier.</param>
    /// <returns>The requested season.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<Season>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Season>> GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid season ID" });
        }

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
    /// <param name="request">Season creation details.</param>
    /// <returns>The created season.</returns>
    [HttpPost]
    [ProducesResponseType<Season>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Season>> Create([FromBody] CreateSeasonRequest request)
    {
        var validationResult = await _createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

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
    /// <param name="id">The season identifier.</param>
    /// <param name="request">The new status.</param>
    /// <returns>Success confirmation.</returns>
    [HttpPut("{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateSeasonStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid season ID" });
        }

        var validationResult = await _statusValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        await GetGrain().UpdateSeasonStatusAsync(id, request.Status);
        _logger.LogInformation("Updated season {SeasonId} status to {Status}", id, request.Status);
        return Ok(new { success = true });
    }

    /// <summary>
    /// End a season (triggers character migration).
    /// </summary>
    /// <param name="id">The season identifier to end.</param>
    /// <returns>Success confirmation.</returns>
    [HttpPost("{id}/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EndSeason(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid season ID" });
        }

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
