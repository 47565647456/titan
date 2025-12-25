using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Titan.API.Controllers;

/// <summary>
/// Admin endpoints for system monitoring and diagnostics.
/// </summary>
[ApiController]
[Route("api/admin/system")]
[Authorize(Policy = "SuperAdmin")]
public class SystemAdminController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<SystemAdminController> _logger;

    public SystemAdminController(
        HealthCheckService healthCheckService,
        ILogger<SystemAdminController> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>
    /// Get detailed system health status.
    /// Returns health check results for all registered services including
    /// database, Redis, Orleans cluster, and other infrastructure components.
    /// </summary>
    /// <remarks>
    /// Unlike the public /health endpoint which returns minimal status,
    /// this endpoint returns full details including individual check names,
    /// their status, and response times.
    /// </remarks>
    [HttpGet("health")]
    [ProducesResponseType(typeof(SystemHealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth()
    {
        var report = await _healthCheckService.CheckHealthAsync();
        
        var response = new SystemHealthResponse
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Checks = report.Entries.Select(e => new HealthCheckResult
            {
                Name = e.Key,
                Status = e.Value.Status.ToString(),
                Duration = e.Value.Duration.TotalMilliseconds,
                Description = e.Value.Description,
                Exception = e.Value.Exception?.Message
            }).ToList()
        };

        _logger.LogDebug("Admin health check: {Status}, {CheckCount} checks", 
            response.Status, response.Checks.Count);

        return Ok(response);
    }
}

/// <summary>
/// Detailed system health response.
/// </summary>
public class SystemHealthResponse
{
    /// <summary>Overall system health status (Healthy, Degraded, Unhealthy)</summary>
    public required string Status { get; set; }
    
    /// <summary>Total time to run all health checks in milliseconds</summary>
    public double TotalDuration { get; set; }
    
    /// <summary>Individual health check results</summary>
    public required List<HealthCheckResult> Checks { get; set; }
}

/// <summary>
/// Individual health check result.
/// </summary>
public class HealthCheckResult
{
    /// <summary>Name of the health check (e.g., "titan-db", "orleans-redis")</summary>
    public required string Name { get; set; }
    
    /// <summary>Status of this check (Healthy, Degraded, Unhealthy)</summary>
    public required string Status { get; set; }
    
    /// <summary>Time to complete this check in milliseconds</summary>
    public double Duration { get; set; }
    
    /// <summary>Optional description of the check result</summary>
    public string? Description { get; set; }
    
    /// <summary>Exception message if the check failed</summary>
    public string? Exception { get; set; }
}
