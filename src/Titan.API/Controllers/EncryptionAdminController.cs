using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.API.Services.Encryption;

namespace Titan.API.Controllers;

/// <summary>
/// Admin endpoints for managing encryption configuration and testing key rotation.
/// </summary>
[ApiController]
[Route("api/admin/encryption")]
[Authorize(Policy = "SuperAdmin")]
public class EncryptionAdminController : ControllerBase
{
    private readonly IEncryptionService _encryptionService;
    private readonly KeyRotationService _keyRotationService;
    private readonly ILogger<EncryptionAdminController> _logger;

    public EncryptionAdminController(
        IEncryptionService encryptionService,
        KeyRotationService keyRotationService,
        ILogger<EncryptionAdminController> logger)
    {
        _encryptionService = encryptionService;
        _keyRotationService = keyRotationService;
        _logger = logger;
    }

    /// <summary>
    /// Get current encryption configuration.
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var config = _encryptionService.GetConfig();
        return Ok(config);
    }

    /// <summary>
    /// Get encryption statistics for a specific user.
    /// </summary>
    [HttpGet("connections/{userId}/stats")]
    public IActionResult GetConnectionStats(string userId)
    {
        var stats = _encryptionService.GetConnectionStats(userId);
        if (stats == null)
        {
            return NotFound(new { message = "User not found or encryption not enabled" });
        }
        return Ok(stats);
    }

    /// <summary>
    /// Force key rotation for a specific user (for testing).
    /// </summary>
    [HttpPost("connections/{userId}/rotate")]
    public async Task<IActionResult> ForceRotation(string userId)
    {
        try
        {
            await _keyRotationService.ForceRotationAsync(userId);
            _logger.LogInformation("Admin forced key rotation for user {UserId}", userId);
            return Ok(new { message = "Key rotation initiated", userId });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Force key rotation for all encrypted connections (for testing).
    /// </summary>
    [HttpPost("rotate-all")]
    public async Task<IActionResult> ForceRotationAll()
    {
        await _keyRotationService.ForceRotationAllAsync();
        _logger.LogInformation("Admin forced key rotation for all connections");
        return Ok(new { message = "Key rotation initiated for all encrypted connections" });
    }

    /// <summary>
    /// Get list of connections that need key rotation.
    /// </summary>
    [HttpGet("connections/needs-rotation")]
    public IActionResult GetConnectionsNeedingRotation()
    {
        var connections = _encryptionService.GetConnectionsNeedingRotation().ToList();
        return Ok(new { connections, count = connections.Count });
    }

    /// <summary>
    /// Set encryption enabled state at runtime (for testing).
    /// Note: This change does not persist across server restarts.
    /// </summary>
    [HttpPost("enabled")]
    public IActionResult SetEnabled([FromBody] SetEnabledRequest request)
    {
        _encryptionService.SetEnabled(request.Enabled);
        _logger.LogInformation("Admin set encryption enabled to {Enabled}", request.Enabled);
        return Ok(new 
        { 
            message = $"Encryption enabled set to {request.Enabled}", 
            enabled = request.Enabled,
            persistent = false,
            warning = "This change will not persist across server restarts"
        });
    }

    /// <summary>
    /// Set encryption required state at runtime (for testing).
    /// Note: This change does not persist across server restarts.
    /// </summary>
    [HttpPost("required")]
    public IActionResult SetRequired([FromBody] SetRequiredRequest request)
    {
        _encryptionService.SetRequired(request.Required);
        _logger.LogInformation("Admin set encryption required to {Required}", request.Required);
        return Ok(new 
        { 
            message = $"Encryption required set to {request.Required}", 
            required = request.Required,
            persistent = false,
            warning = "This change will not persist across server restarts"
        });
    }

    /// <summary>
    /// Remove encryption state for a specific user.
    /// </summary>
    [HttpDelete("connections/{userId}")]
    public IActionResult RemoveConnection(string userId)
    {
        _encryptionService.RemoveConnection(userId);
        _logger.LogInformation("Admin removed encryption state for user {UserId}", userId);
        return NoContent();
    }

    /// <summary>
    /// Get encryption operation metrics.
    /// </summary>
    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var metrics = _encryptionService.GetMetrics();
        return Ok(metrics);
    }

    public record SetEnabledRequest(bool Enabled);
    public record SetRequiredRequest(bool Required);
}

