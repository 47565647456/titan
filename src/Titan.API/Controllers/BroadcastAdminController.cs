using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Models;
using Titan.API.Services;

namespace Titan.API.Controllers;

/// <summary>
/// Admin API for broadcasting server messages to all connected players.
/// </summary>
[ApiController]
[Route("api/admin/broadcast")]
[Tags("Admin - Broadcast")]
[Authorize(Policy = "SuperAdmin")]
public class BroadcastAdminController : ControllerBase
{
    private readonly ServerBroadcastService _broadcastService;
    private readonly IValidator<SendBroadcastRequest> _validator;
    private readonly ILogger<BroadcastAdminController> _logger;

    public BroadcastAdminController(
        ServerBroadcastService broadcastService,
        IValidator<SendBroadcastRequest> validator,
        ILogger<BroadcastAdminController> logger)
    {
        _broadcastService = broadcastService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Broadcast a message to all connected players.
    /// </summary>
    /// <param name="request">The broadcast message details.</param>
    /// <returns>The broadcast message with generated ID and timestamp.</returns>
    [HttpPost]
    [ProducesResponseType<ServerMessage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServerMessage>> SendBroadcast([FromBody] SendBroadcastRequest request)
    {
        var validationResult = await _validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var message = await _broadcastService.SendAsync(
            request.Content,
            request.Type,
            request.Title,
            request.IconId,
            request.DurationSeconds);

        _logger.LogInformation(
            "Admin broadcast sent: {MessageId} - {Type} - {Title}",
            message.MessageId,
            message.Type,
            message.Title ?? "(no title)");

        return Ok(message);
    }
}

/// <summary>
/// Request to send a broadcast message.
/// </summary>
public record SendBroadcastRequest(
    string Content,
    ServerMessageType Type = ServerMessageType.Info,
    string? Title = null,
    string? IconId = null,
    int? DurationSeconds = null);
