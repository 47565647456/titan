using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Titan.API.Config;
using Titan.API.Hubs;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Background service that monitors users and initiates key rotation when thresholds are met.
/// </summary>
public class KeyRotationService : BackgroundService
{
    private readonly IEncryptionService _encryptionService;
    private readonly IHubContext<EncryptionHub> _hubContext;
    private readonly EncryptionOptions _options;
    private readonly ILogger<KeyRotationService> _logger;

    public KeyRotationService(
        IEncryptionService encryptionService,
        IHubContext<EncryptionHub> hubContext,
        IOptions<EncryptionOptions> options,
        ILogger<KeyRotationService> logger)
    {
        _encryptionService = encryptionService;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Encryption is disabled, key rotation service will not run");
            return;
        }

        _logger.LogInformation("Key rotation service started. Interval: {Minutes} minutes, Max messages: {MaxMessages}",
            _options.KeyRotationIntervalMinutes, _options.MaxMessagesPerKey);

        // Check every 30 seconds for users needing rotation
        var checkInterval = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRotateKeysAsync(stoppingToken);
                
                // Proactively clean up any expired previous keys
                _encryptionService.CleanupExpiredPreviousKeys();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during key rotation check");
            }

            await Task.Delay(checkInterval, stoppingToken);
        }
    }

    private async Task CheckAndRotateKeysAsync(CancellationToken cancellationToken)
    {
        // GetConnectionsNeedingRotation returns user IDs (encryption state is per-user)
        var usersNeedingRotation = _encryptionService.GetConnectionsNeedingRotation().ToList();

        if (usersNeedingRotation.Count == 0)
            return;

        _logger.LogDebug("Found {Count} users needing key rotation", usersNeedingRotation.Count);

        foreach (var userId in usersNeedingRotation)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var rotationRequest = await _encryptionService.InitiateKeyRotationAsync(userId);

                // Send rotation request to all connections for this user
                // Note: Clients.User() sends to all connections with matching UserIdentifier
                await _hubContext.Clients.User(userId)
                    .SendAsync("KeyRotation", rotationRequest, cancellationToken);

                _logger.LogInformation("Sent key rotation request to user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initiate key rotation for user {UserId}", userId);
            }
        }
    }

    /// <summary>
    /// Manually trigger key rotation for a specific user (for testing).
    /// </summary>
    public async Task ForceRotationAsync(string userId)
    {
        var rotationRequest = await _encryptionService.InitiateKeyRotationAsync(userId);
        await _hubContext.Clients.User(userId).SendAsync("KeyRotation", rotationRequest);
        _logger.LogInformation("Forced key rotation for user {UserId}", userId);
    }

    /// <summary>
    /// Manually trigger key rotation for all encrypted users (for testing).
    /// </summary>
    public async Task ForceRotationAllAsync()
    {
        // Get all users with encryption enabled
        var allUsers = _encryptionService.GetAllEncryptedUserIds();
        var usersNeedingRotation = _encryptionService.GetConnectionsNeedingRotation();
        
        var allUserIds = allUsers.Concat(usersNeedingRotation).Distinct().ToList();

        foreach (var userId in allUserIds)
        {
            try
            {
                await ForceRotationAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to force rotation for user {UserId}", userId);
            }
        }

        _logger.LogInformation("Forced key rotation for {Count} users", allUserIds.Count);
    }
}

