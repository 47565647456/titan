using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;
using Titan.API.Services.Encryption;

namespace Titan.API.Hubs;

/// <summary>
/// SignalR Hub for encryption key exchange and rotation.
/// Clients connect to this hub to establish encrypted communication.
/// </summary>
[Authorize(AuthenticationSchemes = "SessionTicket")]
public class EncryptionHub : Hub, IEncryptionHubClient
{
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<EncryptionHub> _logger;

    public EncryptionHub(IEncryptionService encryptionService, ILogger<EncryptionHub> logger)
    {
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Get the current encryption configuration.
    /// </summary>
    public Task<EncryptionConfig> GetConfig()
    {
        return Task.FromResult(_encryptionService.GetConfig());
    }

    /// <summary>
    /// Perform ECDH key exchange with the server.
    /// </summary>
    public async Task<KeyExchangeResponse> KeyExchange(KeyExchangeRequest request)
    {
        var userId = GetRequiredUserId();

        _logger.LogInformation("Key exchange initiated by user {UserId}", userId);

        var response = await _encryptionService.PerformKeyExchangeAsync(
            userId,
            request.ClientPublicKey,
            request.ClientSigningPublicKey);

        _logger.LogInformation("Key exchange completed for user {UserId}, KeyId: {KeyId}",
            userId, response.KeyId);

        return response;
    }

    /// <summary>
    /// Complete a key rotation initiated by the server.
    /// </summary>
    public async Task CompleteKeyRotation(KeyRotationAck ack)
    {
        var userId = GetRequiredUserId();
        _logger.LogInformation("Key rotation completion request from user {UserId}", userId);

        await _encryptionService.CompleteKeyRotationAsync(userId, ack);
        _logger.LogInformation("Key rotation completed for user {UserId}", userId);
    }

    private string GetRequiredUserId()
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            throw new HubException("User authentication required for encryption operations");
        }
        return userId;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Note: We do NOT remove encryption state on disconnect because the user
        // may have multiple connections and encryption is per-user, not per-connection.
        // State is cleaned up when the session expires or explicitly by admin.
        return base.OnDisconnectedAsync(exception);
    }
}
