using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Encryption;
using System.Reflection;
using MemoryPack;
using System.Text.Json;

namespace Titan.API.Hubs;

/// <summary>
/// Base class for authenticated hubs with connection lifecycle tracking.
/// Automatically tracks player presence and session logging.
/// </summary>
public abstract class TitanHubBase : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger _logger;

    protected TitanHubBase(IClusterClient clusterClient, IEncryptionService encryptionService, ILogger logger)
    {
        _clusterClient = clusterClient;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the authenticated user's ID from the principal (claims derived from session ticket).
    /// </summary>
    protected Guid GetUserId() => Guid.Parse(Context.UserIdentifier!);

    /// <summary>
    /// Gets the cluster client for grain access.
    /// </summary>
    protected IClusterClient ClusterClient => _clusterClient;

    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier != null)
        {
            var userId = GetUserId();
            var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

            // Track presence (in-memory) - returns true if this is first connection
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            var isFirstConnection = await presenceGrain.RegisterConnectionAsync(Context.ConnectionId, GetType().Name);

            // Log session (persisted) - only on first connection for this user
            if (isFirstConnection)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.StartSessionAsync(ip);
            }

            _logger.LogDebug("User {UserId} connected via {Hub} (first: {IsFirst})", userId, GetType().Name, isFirstConnection);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier != null)
        {
            var userId = GetUserId();
            
            // Track presence (in-memory) - returns true if this was last connection
            var presenceGrain = _clusterClient.GetGrain<IPlayerPresenceGrain>(userId);
            var wasLastConnection = await presenceGrain.UnregisterConnectionAsync(Context.ConnectionId);

            // End session (persisted) - only when last connection closes
            if (wasLastConnection)
            {
                var sessionGrain = _clusterClient.GetGrain<ISessionLogGrain>(userId);
                await sessionGrain.EndSessionAsync();
            }

        _logger.LogDebug("User {UserId} disconnected from {Hub} (last: {IsLast})", userId, GetType().Name, wasLastConnection);
        }
        await base.OnDisconnectedAsync(exception);
    }

    #region Ownership Verification

    /// <summary>
    /// Verifies that the specified character belongs to the authenticated user.
    /// Throws HubException if ownership verification fails.
    /// </summary>
    protected async Task VerifyCharacterOwnershipAsync(Guid characterId)
    {
        var characterIds = await GetOwnedCharacterIdsAsync();
        if (!characterIds.Contains(characterId))
        {
            throw new HubException("Character does not belong to this account.");
        }
    }

    /// <summary>
    /// Gets the set of character IDs owned by the authenticated user.
    /// Useful for batch ownership checks.
    /// </summary>
    protected async Task<HashSet<Guid>> GetOwnedCharacterIdsAsync()
    {
        var accountGrain = ClusterClient.GetGrain<IAccountGrain>(GetUserId());
        var characters = await accountGrain.GetCharactersAsync();
        return characters.Select(c => c.CharacterId).ToHashSet();
    }

    #endregion

    #region Encrypted Communication

    /// <summary>
    /// Gateway for encrypted hub calls.
    /// Decrypts the envelope, finds the target method, and dispatches the call.
    /// </summary>
    [HubMethodName("__encrypted__")]
    public async Task<object?> InvokeEncrypted(SecureEnvelope envelope)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId))
        {
            throw new HubException("User must be authenticated for encrypted calls.");
        }

        // 1. Decrypt and Verify the envelope
        byte[] plaintext;
        try
        {
            plaintext = await _encryptionService.DecryptAndVerifyAsync(userId, envelope);
        }
        catch (System.Security.SecurityException secEx)
        {
            _logger.LogWarning(secEx, "Encrypted invocation failed security verification for user {UserId}", userId);
            throw new HubException("Security verification failed: " + secEx.Message);
        }

        // 2. Deserialize the invocation request
        EncryptedInvocation? invocation = null;
        try
        {
            invocation = MemoryPackSerializer.Deserialize<EncryptedInvocation>(plaintext);
        }
        catch (Exception ex)
        { 
            _logger.LogDebug(ex, "MemoryPack deserialization of EncryptedInvocation failed, falling back to JSON");
        }

        if (invocation == null)
        {
            try
            {
                var jsonWrapper = System.Text.Encoding.UTF8.GetString(plaintext);
                invocation = JsonSerializer.Deserialize<EncryptedInvocation>(jsonWrapper, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize EncryptedInvocation for user {UserId}", userId);
                throw new HubException("Invalid encrypted invocation format.");
            }
        }

        if (invocation == null)
        {
             throw new HubException("Invalid encrypted invocation format.");
        }

        // 3. Find the target method
        var method = GetType().GetMethod(invocation.Target, 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly);
        
        if (method == null || method.Name == nameof(InvokeEncrypted) || method.GetCustomAttribute<HubMethodNameAttribute>()?.Name == "__encrypted__")
        {
            throw new HubException($"Method '{invocation.Target}' not found on hub '{GetType().Name}'.");
        }

        // 4. Deserialize arguments based on method signature
        var parameters = method.GetParameters();
        object?[] args;
        
        try
        {
            if (parameters.Length == 0)
            {
                args = Array.Empty<object>();
            }
            else
            {
                // MemoryPack can't easily deserialize a "dynamic" array of types from one byte[] 
                // UNLESS we serialized it as a Tuple or a specific record.
                // For flexibility, we use a dedicated helper to deserialize the payload.
                args = DeserializeArguments(invocation.Payload, parameters);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize arguments for {Hub}.{Method}", GetType().Name, method.Name);
            throw new HubException("Failed to parse method arguments.");
        }

        // 5. Invoke the method
        try
        {
            var result = method.Invoke(this, args);
            
            // Handle Task/ValueTask results
            if (result is Task task)
            {
                await task;
                
                // Get result value if it's Task<T>
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }
            else if (result != null && result.GetType().IsGenericType && 
                     result.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                // Handle ValueTask<T>
                var asTask = (Task)result.GetType().GetMethod("AsTask")!.Invoke(result, null)!;
                await asTask;
                var resultProperty = asTask.GetType().GetProperty("Result");
                return resultProperty?.GetValue(asTask);
            }
            else if (result is ValueTask valueTask)
            {
                await valueTask;
                return null;
            }
            
            return result;
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogError(ex.InnerException, "Error in {Hub}.{Method}", GetType().Name, method.Name);
            throw ex.InnerException ?? ex;
        }
    }

    private object?[] DeserializeArguments(byte[] payload, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0) return Array.Empty<object>();

        // Try MemoryPack first (used by C# client with single arg)
        if (parameters.Length == 1)
        {
            try
            {
                var arg = MemoryPackSerializer.Deserialize(parameters[0].ParameterType, payload);
                return new[] { arg };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MemoryPack deserialization failed for type {TypeName}, falling back to JSON", 
                    parameters[0].ParameterType.Name);
            }
        }

        // Handle JSON (TS/Dashboard client)
        var json = System.Text.Encoding.UTF8.GetString(payload);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (parameters.Length == 1)
        {
            var arg = JsonSerializer.Deserialize(json, parameters[0].ParameterType, options);
            return new[] { arg };
        }

        // Multi-argument calls: client should send a JSON array
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new HubException("Multi-argument encrypted call must provide arguments as a JSON array.");
            }

            var array = doc.RootElement;
            if (array.GetArrayLength() != parameters.Length)
            {
                throw new HubException($"Argument count mismatch. Expected {parameters.Length}, got {array.GetArrayLength()}.");
            }

            var result = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                result[i] = JsonSerializer.Deserialize(array[i].GetRawText(), parameters[i].ParameterType, options);
            }
            return result;
        }
        catch (Exception ex)
        {
            var parameterList = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            _logger.LogError(ex, "Failed to deserialize multi-argument payload for {Hub}.{Method}({ParameterList})", 
                GetType().Name, parameters[0].Member.Name, parameterList);
            throw new HubException("Invalid argument format for multi-parameter call.");
        }
    }

    #endregion
}
