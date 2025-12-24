using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Titan.Client;
using Titan.Client.Encryption;

namespace Titan.AppHost.Tests;

/// <summary>
/// Shared test helpers for encryption integration tests.
/// Reduces code duplication across EncryptionIntegrationTests and EncryptedBroadcastTests.
/// </summary>
public static class EncryptionIntegrationHelpers
{
    /// <summary>
    /// Polls a condition until it returns true or timeout is reached.
    /// </summary>
    public static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50); // Small delay between polls
        }
        throw new TimeoutException($"Timed out waiting for: {description}");
    }

    /// <summary>
    /// Captures the current encryption state and provides methods to toggle encryption.
    /// Implements IAsyncDisposable to restore original state automatically.
    /// </summary>
    public static async Task<EncryptionStateContext> CaptureEncryptionStateAsync(HttpClient adminClient)
    {
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var config = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        return new EncryptionStateContext(adminClient, config?.Enabled ?? false, config?.Required ?? false);
    }

    /// <summary>
    /// Creates a hub connection for the encryption hub and performs key exchange.
    /// Properly disposes resources on failure.
    /// </summary>
    public static async Task<(HubConnection Hub, ClientEncryptor Encryptor)> SetupEncryptedConnectionAsync(
        string apiBaseUrl,
        string sessionId)
    {
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/hub/encryption?access_token={sessionId}")
            .Build();
        
        ClientEncryptor? encryptor = null;
        try
        {
            await encryptionHub.StartAsync();

            encryptor = new ClientEncryptor();
            await encryptor.PerformKeyExchangeAsync(async req =>
                await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));

            return (encryptionHub, encryptor);
        }
        catch
        {
            // Dispose resources on failure
            encryptor?.Dispose();
            await encryptionHub.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// Response record for encryption config endpoint.
/// </summary>
public record EncryptionConfigResponse(bool Enabled, bool Required);

/// <summary>
/// Response record for connections needing rotation endpoint.
/// </summary>
public record ConnectionsNeedingRotationResponse(List<string> Connections, int Count);

/// <summary>
/// Response record for rotate-all endpoint.
/// </summary>
public record RotationAllResponse(string Message);

/// <summary>
/// Response record for encryption metrics endpoint.
/// </summary>
public record EncryptionMetricsResponse(
    long KeyExchangesPerformed,
    long MessagesEncrypted,
    long MessagesDecrypted,
    long KeyRotationsTriggered,
    long KeyRotationsCompleted,
    long EncryptionFailures,
    long DecryptionFailures,
    long ExpiredKeysCleanedUp);

/// <summary>
/// Context for managing encryption state during tests.
/// Automatically restores original state when disposed.
/// </summary>
public sealed class EncryptionStateContext : IAsyncDisposable
{
    private readonly HttpClient _adminClient;
    private readonly bool _originalEnabled;
    private readonly bool _originalRequired;

    public bool WasEnabled => _originalEnabled;
    public bool WasRequired => _originalRequired;

    public EncryptionStateContext(HttpClient adminClient, bool originalEnabled, bool originalRequired)
    {
        _adminClient = adminClient;
        _originalEnabled = originalEnabled;
        _originalRequired = originalRequired;
    }

    /// <summary>
    /// Enables encryption via the admin API.
    /// </summary>
    public async Task EnableEncryptionAsync()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = true });
    }

    /// <summary>
    /// Disables encryption via the admin API.
    /// </summary>
    public async Task DisableEncryptionAsync()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = false });
    }

    /// <summary>
    /// Sets whether encryption is required via the admin API.
    /// </summary>
    public async Task SetRequiredAsync(bool required)
    {
        await _adminClient.PostAsJsonAsync("/api/admin/encryption/required", new { Required = required });
    }

    /// <summary>
    /// Restores the original encryption state.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = _originalEnabled });
        // Always restore the Required state regardless of its original value
        await _adminClient.PostAsJsonAsync("/api/admin/encryption/required", new { Required = _originalRequired });
    }
}
