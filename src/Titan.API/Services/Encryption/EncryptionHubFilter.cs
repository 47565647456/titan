using MemoryPack;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Titan.Abstractions.Models;
using Titan.API.Config;

namespace Titan.API.Services.Encryption;

/// <summary>
/// SignalR Hub filter that handles encryption of hub method return values.
/// For INBOUND messages, clients should use EncryptedHubProtocol which handles
/// full message encryption/decryption at the wire level.
/// This filter encrypts OUTBOUND return values for encrypted users.
/// Encryption state is keyed by userId to enable cross-hub encryption.
/// </summary>
public class EncryptionHubFilter : IHubFilter
{
    private readonly IEncryptionService _encryptionService;
    private readonly EncryptionOptions _options;
    private readonly ILogger<EncryptionHubFilter> _logger;

    // Hub methods that should NOT be encrypted (encryption-related methods)
    private static readonly HashSet<string> ExcludedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        // EncryptionHub methods
        "KeyExchange",
        "KeyRotationAck",
        "GetConfig",
        // Standard SignalR methods
        "OnConnectedAsync",
        "OnDisconnectedAsync"
    };

    // Hubs where encryption should not apply
    private static readonly HashSet<string> ExcludedHubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "EncryptionHub"  // Don't encrypt the encryption hub itself
    };

    public EncryptionHubFilter(
        IEncryptionService encryptionService,
        IOptions<EncryptionOptions> options,
        ILogger<EncryptionHubFilter> logger)
    {
        _encryptionService = encryptionService;
        _options = options.Value;
        _logger = logger;
    }

    private static bool IsVoidResult(object result)
    {
        var type = result.GetType();
        // Check for Task without result (non-generic Task completes without value)
        if (result is Task && !type.IsGenericType)
            return true;
        // VoidTaskResult is internal, check by name as fallback
        return type.Name == "VoidTaskResult";
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var userId = context.Context.UserIdentifier;
        var methodName = context.HubMethodName;
        var hubTypeName = context.Hub.GetType().Name;

        // Skip encryption for excluded hubs and methods (like KeyExchange)
        if (ExcludedHubs.Contains(hubTypeName) || ExcludedMethods.Contains(methodName))
        {
            return await next(context);
        }

        // Check if encryption is enabled globally
        var config = _encryptionService.GetConfig();
        if (!config.Enabled)
        {
            return await next(context);
        }

        // User must be authenticated for encryption
        if (string.IsNullOrEmpty(userId))
        {
            return await next(context);
        }

        // Check if encryption is enabled for this user (they've done key exchange)
        var isEncryptedUser = _encryptionService.IsEncryptionEnabled(userId);

        if (!isEncryptedUser)
        {
            // If encryption is required but not enabled for this user, reject
            if (config.Required)
            {
                _logger.LogWarning("User {UserId} attempted {Hub}.{Method} without completing key exchange",
                    userId, hubTypeName, methodName);
                throw new HubException("Encryption is required. Please complete key exchange via EncryptionHub first.");
            }
            // Allow unencrypted call if not strictly required
            return await next(context);
        }

        // User IS encrypted. Enforce that they use the __encrypted__ gateway.
        // Note: methodName contains the actual method name ("InvokeEncrypted"), not the [HubMethodName] attribute value.
        // We need to check for both since clients call "__encrypted__" but SignalR resolves it to the actual method name.
        bool isGatewayCall = methodName.Equals("__encrypted__", StringComparison.OrdinalIgnoreCase)
                          || methodName.Equals("InvokeEncrypted", StringComparison.OrdinalIgnoreCase);
        
        if (!isGatewayCall && config.Required)
        {
            _logger.LogWarning("User {UserId} sent plaintext call to {Hub}.{Method} instead of encrypted gateway",
                userId, hubTypeName, methodName);
            throw new HubException("Encryption is required. Use the encrypted gateway (__encrypted__) for this call.");
        }

        try
        {
            // Call the actual hub method (could be the gateway or an unencrypted method if allowed)
            var result = await next(context);

            // Automatically encrypt the response for any call to an encrypted user
            // This ensures even discovery/unencrypted calls that return data are protected if keys exist
            if (result != null && !IsVoidResult(result))
            {
                // Serialize result to MemoryPack bytes
                var plaintext = MemoryPackSerializer.Serialize(result.GetType(), result);

                // If it was a gateway call, use the client's key for the response to avoid race conditions
                // during simultaneous connections (like React Strict Mode double-mount)
                string? keyIdHint = null;
                if (isGatewayCall && context.HubMethodArguments.Count > 0 && context.HubMethodArguments[0] is SecureEnvelope incomingEnvelope)
                {
                    keyIdHint = incomingEnvelope.KeyId;
                }

                // Encrypt and sign
                var encryptedResult = await _encryptionService.EncryptAndSignAsync(userId, plaintext, keyIdHint);

                _logger.LogInformation("Encrypted response ({Bytes} bytes) for {Hub}.{Method} to user {UserId}",
                    plaintext.Length, hubTypeName, methodName, userId);

                return encryptedResult;
            }

            return result;
        }
        catch (System.Security.SecurityException secEx)
        {
            _logger.LogWarning(secEx, "Security error for {Hub}.{Method} on user {UserId}: {Message}",
                hubTypeName, methodName, userId, secEx.Message);
            throw new HubException("Security error: " + secEx.Message);
        }
        catch (Exception ex) when (ex is not HubException)
        {
            _logger.LogError(ex, "Encryption/decryption error for {Hub}.{Method} on user {UserId}",
                hubTypeName, methodName, userId);
            throw new HubException("Encryption error: " + ex.Message);
        }
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        _logger.LogInformation("Connection {ConnectionId} (user {UserId}) connected to {Hub}",
            context.Context.ConnectionId, context.Context.UserIdentifier, context.Hub.GetType().Name);
        return next(context);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        // Note: We do NOT remove encryption state on disconnect because encryption
        // is per-user, not per-connection. A user may have multiple connections.
        // State is cleaned up when the session expires or explicitly by admin.
        
        _logger.LogInformation("Connection {ConnectionId} (user {UserId}) disconnected from {Hub}",
            context.Context.ConnectionId, context.Context.UserIdentifier, context.Hub.GetType().Name);
        
        return next(context, exception);
    }
}
