using MemoryPack;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.Client.Encryption;

/// <summary>
/// Wrapper around HubConnection that transparently encrypts outgoing calls
/// and decrypts incoming responses when encryption is enabled.
/// </summary>
public class EncryptedHubConnection : IAsyncDisposable
{
    private readonly HubConnection _inner;
    private readonly IClientEncryptor? _encryptor;
    private readonly bool _encryptionEnabled;

    public EncryptedHubConnection(HubConnection inner, IClientEncryptor? encryptor, bool encryptionEnabled)
    {
        _inner = inner;
        _encryptor = encryptor;
        _encryptionEnabled = encryptionEnabled && encryptor?.IsInitialized == true;
    }

    /// <summary>
    /// The underlying hub connection.
    /// </summary>
    public HubConnection Inner => _inner;

    /// <summary>
    /// Whether this connection uses encryption.
    /// </summary>
    public bool IsEncrypted => _encryptionEnabled;

    /// <summary>
    /// Connection state of the underlying hub.
    /// </summary>
    public HubConnectionState State => _inner.State;

    /// <summary>
    /// Invoke a hub method with optional encryption.
    /// Supports variadic arguments which are automatically wrapped for encryption if active.
    /// </summary>
    public async Task<TResult> InvokeAsync<TResult>(string methodName, params object?[] args)
    {
        if (!_encryptionEnabled || _encryptor == null)
        {
            // No encryption - call directly
            return await _inner.InvokeCoreAsync<TResult>(methodName, args);
        }

        // Encrypt the argument(s)
        // If single arg, serialize directly. If multi, serialize as array.
        byte[] payload;
        if (args.Length == 0)
        {
            payload = Array.Empty<byte>();
        }
        else if (args.Length == 1)
        {
            payload = args[0] == null ? Array.Empty<byte>() : MemoryPackSerializer.Serialize(args[0]!.GetType(), args[0]);
        }
        else
        {
            // Multi-argument: use JSON array for cross-platform compatibility with the gateway
            var json = System.Text.Json.JsonSerializer.Serialize(args);
            payload = System.Text.Encoding.UTF8.GetBytes(json);
        }

        var invocation = new EncryptedInvocation(methodName, payload);
        
        // Serialize the invocation wrapper
        var invocationBytes = MemoryPackSerializer.Serialize(invocation);
        var envelope = _encryptor.EncryptAndSign(invocationBytes);

        // Call the generic encrypted gateway
        var result = await _inner.InvokeAsync<object?>("__encrypted__", envelope);

        // Check if result is a SecureEnvelope (encrypted response)
        if (result is SecureEnvelope responseEnvelope)
        {
            var decrypted = _encryptor.DecryptAndVerify(responseEnvelope);
            return MemoryPackSerializer.Deserialize<TResult>(decrypted) 
                ?? throw new InvalidOperationException("Failed to deserialize encrypted response");
        }

        // Handle JsonElement (SignalR JSON protocol returns this for object type)
        if (result is System.Text.Json.JsonElement jsonElement)
        {
            try
            {
                var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var envelopeFromJson = System.Text.Json.JsonSerializer.Deserialize<SecureEnvelope>(jsonElement.GetRawText(), jsonOptions);
                if (envelopeFromJson != null && !string.IsNullOrEmpty(envelopeFromJson.KeyId))
                {
                    var decrypted = _encryptor.DecryptAndVerify(envelopeFromJson);
                    return MemoryPackSerializer.Deserialize<TResult>(decrypted) 
                        ?? throw new InvalidOperationException("Failed to deserialize encrypted response");
                }
            }
            catch (System.Text.Json.JsonException) { }
            
            var fallbackOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return System.Text.Json.JsonSerializer.Deserialize<TResult>(jsonElement.GetRawText(), fallbackOptions)
                ?? throw new InvalidOperationException("Failed to deserialize response");
        }

        if (result is TResult typedResult) return typedResult;
        throw new InvalidOperationException($"Unexpected response type: {result?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// Send a hub method without waiting for a response.
    /// </summary>
    public async Task SendAsync(string methodName, params object?[] args)
    {
        if (!_encryptionEnabled || _encryptor == null)
        {
            await _inner.SendCoreAsync(methodName, args);
            return;
        }

        byte[] payload;
        if (args.Length == 0) payload = Array.Empty<byte>();
        else if (args.Length == 1) payload = args[0] == null ? Array.Empty<byte>() : MemoryPackSerializer.Serialize(args[0]!.GetType(), args[0]);
        else payload = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(args));

        var invocation = new EncryptedInvocation(methodName, payload);
        var invocationBytes = MemoryPackSerializer.Serialize(invocation);
        var envelope = _encryptor.EncryptAndSign(invocationBytes);
        
        await _inner.SendAsync("__encrypted__", envelope);
    }

    /// <summary>
    /// Register a handler for messages from the server.
    /// Automatically decrypts if the message is a SecureEnvelope.
    /// </summary>
    public IDisposable On<T>(string methodName, Action<T> handler)
    {
        if (!_encryptionEnabled || _encryptor == null)
        {
            return _inner.On(methodName, handler);
        }

        // Register handler that can receive either encrypted or plain messages
        return _inner.On<object>(methodName, message =>
        {
            T result;
            if (message is SecureEnvelope envelope)
            {
                var decrypted = _encryptor.DecryptAndVerify(envelope);
                result = MemoryPackSerializer.Deserialize<T>(decrypted)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted message");
            }
            else if (message is T typed)
            {
                result = typed;
            }
            else
            {
                throw new InvalidOperationException($"Unexpected message type: {message?.GetType().Name ?? "null"}");
            }

            handler(result);
        });
    }

    /// <summary>
    /// Register an async handler for messages from the server.
    /// </summary>
    public IDisposable On<T>(string methodName, Func<T, Task> handler)
    {
        if (!_encryptionEnabled || _encryptor == null)
        {
            return _inner.On(methodName, handler);
        }

        return _inner.On<object>(methodName, async message =>
        {
            T result;
            if (message is SecureEnvelope envelope)
            {
                var decrypted = _encryptor.DecryptAndVerify(envelope);
                result = MemoryPackSerializer.Deserialize<T>(decrypted)
                    ?? throw new InvalidOperationException("Failed to deserialize encrypted message");
            }
            else if (message is T typed)
            {
                result = typed;
            }
            else
            {
                throw new InvalidOperationException($"Unexpected message type: {message?.GetType().Name ?? "null"}");
            }

            await handler(result);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
    }
}
