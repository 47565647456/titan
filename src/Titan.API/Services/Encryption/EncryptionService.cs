using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Titan.Abstractions.Models;
using Titan.API.Config;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Implementation of application-layer payload encryption using AES-256-GCM and ECDSA.
/// Manages per-user encryption state with key exchange and rotation support.
/// Persists signing keys and encryption state to Redis for resilience across restarts.
/// </summary>
public class EncryptionService : IEncryptionService, IDisposable
{
    private readonly EncryptionOptions _options;
    private readonly ILogger<EncryptionService> _logger;
    private readonly EncryptionMetrics _metrics;
    private readonly EncryptionStateStore? _stateStore;
    private readonly ConcurrentDictionary<string, ConnectionEncryptionState> _connections = new();

    // Runtime toggle for encryption (can be changed via admin API)
    private bool _runtimeEnabled;
    private bool _runtimeRequired;

    // Server's long-term signing keypair (loaded from Redis or generated on first use)
    private ECDsa? _serverSigningKey;
    private byte[]? _serverSigningPublicKey;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public EncryptionService(
        IOptions<EncryptionOptions> options, 
        ILogger<EncryptionService> logger,
        EncryptionMetrics metrics,
        EncryptionStateStore? stateStore = null)
    {
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        _stateStore = stateStore;

        // Initialize runtime state from config
        _runtimeEnabled = _options.Enabled;
        _runtimeRequired = _options.RequireEncryption;

        _logger.LogInformation("EncryptionService initialized. Enabled: {Enabled}, Required: {Required}, Persistence: {HasStore}",
            _runtimeEnabled, _runtimeRequired, stateStore != null);
    }

    /// <summary>
    /// Ensures the signing key is initialized (loads from Redis or generates new).
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            if (_stateStore != null)
            {
                // Try to load signing key from Redis
                var storedKey = await _stateStore.LoadSigningKeyAsync();
                if (storedKey != null)
                {
                    _serverSigningKey = ECDsa.Create();
                    _serverSigningKey.ImportECPrivateKey(storedKey, out _);
                    _serverSigningPublicKey = _serverSigningKey.ExportSubjectPublicKeyInfo();
                    _logger.LogInformation("Loaded server signing key from Redis");
                }
                else
                {
                    // Generate new key and save to Redis
                    _serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    _serverSigningPublicKey = _serverSigningKey.ExportSubjectPublicKeyInfo();
                    await _stateStore.SaveSigningKeyAsync(_serverSigningKey.ExportECPrivateKey());
                    _logger.LogInformation("Generated and saved new server signing key to Redis");
                }

                // Load any persisted encryption states
                var states = await _stateStore.LoadAllEncryptionStatesAsync();
                foreach (var (userId, persistedState) in states)
                {
                    _connections[userId] = ConnectionEncryptionState.FromPersisted(persistedState);
                }
                if (states.Count > 0)
                {
                    _logger.LogInformation("Restored {Count} encryption states from Redis", states.Count);
                }
            }
            else
            {
                // No persistence - generate in-memory key
                _serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                _serverSigningPublicKey = _serverSigningKey.ExportSubjectPublicKeyInfo();
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public EncryptionConfig GetConfig() => new(_runtimeEnabled, _runtimeRequired);

    public void SetEnabled(bool enabled)
    {
        var previous = _runtimeEnabled;
        _runtimeEnabled = enabled;
        _logger.LogInformation("Encryption enabled state changed: {Previous} -> {Current}", previous, enabled);
    }

    public void SetRequired(bool required)
    {
        var previous = _runtimeRequired;
        _runtimeRequired = required;
        _logger.LogInformation("Encryption required state changed: {Previous} -> {Current}", previous, required);
    }

    /// <summary>
    /// Synchronous key exchange (for backwards compatibility).
    /// Prefer PerformKeyExchangeAsync to avoid potential deadlocks.
    /// </summary>
    [Obsolete("Use PerformKeyExchangeAsync to avoid potential deadlocks")]
    public KeyExchangeResponse PerformKeyExchange(
        string userId,
        byte[] clientPublicKey,
        byte[] clientSigningPublicKey)
        => PerformKeyExchangeAsync(userId, clientPublicKey, clientSigningPublicKey).GetAwaiter().GetResult();

    public async Task<KeyExchangeResponse> PerformKeyExchangeAsync(
        string userId,
        byte[] clientPublicKey,
        byte[] clientSigningPublicKey)
    {
        await EnsureInitializedAsync();
        
        // Generate ephemeral ECDH keypair for this connection
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var serverPublicKey = serverEcdh.ExportSubjectPublicKeyInfo();

        // Import client's public key and derive shared secret
        using var clientEcdh = ECDiffieHellman.Create();
        clientEcdh.ImportSubjectPublicKeyInfo(clientPublicKey, out _);

        var sharedSecret = serverEcdh.DeriveRawSecretAgreement(clientEcdh.PublicKey);
        
        // Generate cryptographically random salt for HKDF (32 bytes for SHA-256)
        var hkdfSalt = RandomNumberGenerator.GetBytes(32);
        
        // Derive AES key from shared secret using HKDF with salt
        var aesKey = DeriveKey(sharedSecret, hkdfSalt, "titan-encryption-key", 32);
        
        // Zero out shared secret immediately after key derivation
        CryptographicOperations.ZeroMemory(sharedSecret);

        // Generate key ID
        var keyId = GenerateKeyId();

        // Import client's signing public key
        using var clientSigningKey = ECDsa.Create();
        clientSigningKey.ImportSubjectPublicKeyInfo(clientSigningPublicKey, out _);

        // Store connection state
        var state = new ConnectionEncryptionState
        {
            KeyId = keyId,
            AesKey = aesKey,
            HkdfSalt = hkdfSalt,
            ClientSigningPublicKey = clientSigningPublicKey,
            UserIdHash = ComputeConnectionHash(userId),
            NonceCounter = 0,
            MessageCount = 0,
            KeyCreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        // Store connection state with grace period for previous key
        _connections.AddOrUpdate(userId,
            _ => state,
            (_, oldState) =>
            {
                state.PreviousKeyId = oldState.KeyId;
                state.PreviousAesKey = oldState.AesKey;
                state.PreviousClientSigningPublicKey = oldState.ClientSigningPublicKey;
                state.PreviousKeyExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.KeyRotationGracePeriodSeconds);
                return state;
            });

        // Persist state to Redis (fire-and-forget with error logging)
        _stateStore?.SaveEncryptionStateAsync(userId, state.ToPersisted())
            .ContinueWith(t => 
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Failed to persist encryption state for user {UserId}", userId);
            }, TaskContinuationOptions.OnlyOnFaulted);

        _metrics.IncrementKeyExchanges();
        _logger.LogDebug("Key exchange completed for connection {userId}, KeyId: {KeyId}",
            userId, keyId);

        return new KeyExchangeResponse(keyId, serverPublicKey, _serverSigningPublicKey!, hkdfSalt);
    }

    public byte[] DecryptAndVerify(string userId, SecureEnvelope envelope)
    {
        if (!_connections.TryGetValue(userId, out var state))
        {
            throw new InvalidOperationException("Connection has not completed key exchange");
        }

        // Validate key ID (support current and previous key during rotation grace period)
        if (envelope.KeyId != state.KeyId && envelope.KeyId != state.PreviousKeyId)
        {
            throw new SecurityException($"Invalid key ID: {envelope.KeyId}");
        }

        // Lock during key selection to prevent cleanup from zeroing the key we're about to use
        byte[] aesKey;
        byte[] signingKey;
        lock (state.StateLock)
        {
            // Check if previous key has expired (enforce grace period)
            if (envelope.KeyId == state.PreviousKeyId && state.PreviousKeyExpiresAt.HasValue)
            {
                if (DateTimeOffset.UtcNow > state.PreviousKeyExpiresAt.Value)
                {
                    // Clean up expired previous key
                    if (state.PreviousAesKey != null)
                    {
                        CryptographicOperations.ZeroMemory(state.PreviousAesKey);
                        state.PreviousAesKey = null;
                    }
                    state.PreviousKeyId = null;
                    state.PreviousKeyExpiresAt = null;
                    throw new SecurityException("Previous key has expired. Please complete key rotation.");
                }
            }

            // Select appropriate key - copy references while holding lock
            var selectedKey = envelope.KeyId == state.KeyId ? state.AesKey : state.PreviousAesKey;
            if (selectedKey == null)
            {
                throw new SecurityException("Key not available for decryption");
            }
            aesKey = selectedKey;
            
            // Determine which signing key to use
            signingKey = envelope.KeyId == state.PreviousKeyId && state.PreviousClientSigningPublicKey != null
                ? state.PreviousClientSigningPublicKey
                : state.ClientSigningPublicKey;
        }

        // Validate timestamp for replay protection
        var messageTime = DateTimeOffset.FromUnixTimeMilliseconds(envelope.Timestamp);
        var age = DateTimeOffset.UtcNow - messageTime;
        if (age.TotalSeconds > _options.ReplayWindowSeconds || age.TotalSeconds < -5)
        {
            throw new SecurityException($"Message timestamp outside valid window: {age.TotalSeconds}s");
        }

        // Validate sequence number per keyId (to handle key rotation correctly)
        var lastSeqForKey = state.GetLastSequenceForKey(envelope.KeyId);
        if (envelope.SequenceNumber <= lastSeqForKey)
        {
            throw new SecurityException($"Sequence number regression detected for key {envelope.KeyId}: {envelope.SequenceNumber} <= {lastSeqForKey}");
        }

        // Verify signature (signingKey was selected inside lock above)
        if (!VerifySignature(envelope, signingKey))
        {
            throw new SecurityException("Signature verification failed");
        }

        // Decrypt
        using var aesGcm = new AesGcm(aesKey, 16);
        var plaintext = new byte[envelope.Ciphertext.Length];
        aesGcm.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext);

        // Update state atomically to prevent race conditions
        state.SetLastSequenceForKey(envelope.KeyId, envelope.SequenceNumber);
        Interlocked.Increment(ref state.MessageCount);
        state.LastActivityAt = DateTimeOffset.UtcNow;

        _metrics.IncrementMessagesDecrypted();
        return plaintext;
    }

    public Task<byte[]> DecryptAndVerifyAsync(string userId, SecureEnvelope envelope)
        => Task.FromResult(DecryptAndVerify(userId, envelope));

    public Task<SecureEnvelope> EncryptAndSignAsync(string userId, byte[] plaintext, string? keyId = null)
        => Task.FromResult(EncryptAndSign(userId, plaintext, keyId));

    public SecureEnvelope EncryptAndSign(string userId, byte[] plaintext, string? keyId = null)
    {
        if (!_connections.TryGetValue(userId, out var state))
        {
            throw new InvalidOperationException("Connection has not completed key exchange");
        }

        // Determine which key to use (support conversational symmetry with keyId hints)
        var aesKey = state.AesKey;
        var actualKeyId = state.KeyId;

        if (keyId != null && keyId == state.PreviousKeyId && state.PreviousAesKey != null)
        {
            aesKey = state.PreviousAesKey;
            actualKeyId = state.PreviousKeyId;
        }

        // Generate nonce: [4 bytes connection hash][8 bytes counter]
        var nonce = new byte[12];
        BitConverter.TryWriteBytes(nonce.AsSpan(0, 4), state.UserIdHash);
        BitConverter.TryWriteBytes(nonce.AsSpan(4, 8), Interlocked.Increment(ref state.NonceCounter));

        // Encrypt
        using var aesGcm = new AesGcm(aesKey, 16);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sequenceNumber = Interlocked.Increment(ref state.ServerSequenceNumber);

        // Sign
        var signature = CreateSignature(actualKeyId, nonce, ciphertext, tag, timestamp, sequenceNumber);

        Interlocked.Increment(ref state.MessageCount);
        state.LastActivityAt = DateTimeOffset.UtcNow;

        _metrics.IncrementMessagesEncrypted();

        return new SecureEnvelope
        {
            KeyId = actualKeyId,
            Nonce = nonce,
            Ciphertext = ciphertext,
            Tag = tag,
            Signature = signature,
            Timestamp = timestamp,
            SequenceNumber = sequenceNumber
        };
    }

    public Task<SecureEnvelope> EncryptAndSignAsync(string userId, byte[] plaintext)
        => Task.FromResult(EncryptAndSign(userId, plaintext));

    public KeyRotationRequest InitiateKeyRotation(string userId)
    {
        if (!_connections.TryGetValue(userId, out var state))
        {
            throw new InvalidOperationException("Connection has not completed key exchange");
        }

        // Generate new ECDH keypair
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var serverPublicKey = serverEcdh.ExportSubjectPublicKeyInfo();

        var newKeyId = GenerateKeyId();
        
        // Generate salt upfront so client can derive the same key
        var newHkdfSalt = RandomNumberGenerator.GetBytes(32);

        // Store rotation state (will complete when client responds)
        state.PendingRotationKeyId = newKeyId;
        state.PendingRotationEcdhPrivateKey = serverEcdh.ExportECPrivateKey();
        state.PendingRotationHkdfSalt = newHkdfSalt;

        _metrics.IncrementKeyRotationsTriggered();
        _logger.LogInformation("Initiated key rotation for connection {userId}, NewKeyId: {KeyId}",
            userId, newKeyId);

        return new KeyRotationRequest(newKeyId, serverPublicKey, newHkdfSalt);
    }

    public Task<KeyRotationRequest> InitiateKeyRotationAsync(string userId)
        => Task.FromResult(InitiateKeyRotation(userId));

    public void CompleteKeyRotation(string userId, KeyRotationAck ack)
    {
        if (!_connections.TryGetValue(userId, out var state))
        {
            throw new InvalidOperationException("Connection has not completed key exchange");
        }

        // Lock to prevent TOCTOU race if multiple threads call CompleteKeyRotation concurrently
        lock (state.StateLock)
        {
            if (state.PendingRotationKeyId == null || state.PendingRotationEcdhPrivateKey == null || state.PendingRotationHkdfSalt == null)
            {
                throw new InvalidOperationException("No pending key rotation for this connection");
            }

            // Recreate server ECDH from stored private key
            using var serverEcdh = ECDiffieHellman.Create();
            serverEcdh.ImportECPrivateKey(state.PendingRotationEcdhPrivateKey, out _);

            // Import client's new public key
            using var clientEcdh = ECDiffieHellman.Create();
            clientEcdh.ImportSubjectPublicKeyInfo(ack.ClientPublicKey.Span, out _);

            var sharedSecret = serverEcdh.DeriveRawSecretAgreement(clientEcdh.PublicKey);
            
            // Use the salt that was generated during InitiateKeyRotation
            var newAesKey = DeriveKey(sharedSecret, state.PendingRotationHkdfSalt, "titan-encryption-key", 32);
            
            // Zero out shared secret immediately after key derivation
            CryptographicOperations.ZeroMemory(sharedSecret);

            // Rotate keys (keep previous for grace period)
            state.PreviousKeyId = state.KeyId;
            state.PreviousAesKey = state.AesKey;
            state.PreviousClientSigningPublicKey = state.ClientSigningPublicKey;
            state.PreviousKeyExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.KeyRotationGracePeriodSeconds);

            state.KeyId = state.PendingRotationKeyId;
            state.AesKey = newAesKey;
            state.HkdfSalt = state.PendingRotationHkdfSalt;
            // Update client signing key to the one from the rotation ack
            // This handles React Strict Mode double-mount where different instances have different signing keys
            state.ClientSigningPublicKey = ack.ClientSigningPublicKey.ToArray();
            state.KeyCreatedAt = DateTimeOffset.UtcNow;
            state.MessageCount = 0;
            state.NonceCounter = 0;

            // Clear pending state
            state.PendingRotationKeyId = null;
            state.PendingRotationEcdhPrivateKey = null;
            state.PendingRotationHkdfSalt = null;

            _metrics.IncrementKeyRotationsCompleted();
            _logger.LogInformation("Completed key rotation for connection {userId}, KeyId: {KeyId}",
                userId, state.KeyId);
        }
    }

    public Task CompleteKeyRotationAsync(string userId, KeyRotationAck ack)
    {
        CompleteKeyRotation(userId, ack);
        return Task.CompletedTask;
    }

    public bool IsEncryptionEnabled(string userId) => _connections.ContainsKey(userId);

    public bool NeedsKeyRotation(string userId)
    {
        if (!_connections.TryGetValue(userId, out var state))
            return false;

        // Check time-based rotation
        var keyAge = DateTimeOffset.UtcNow - state.KeyCreatedAt;
        if (keyAge.TotalMinutes >= _options.KeyRotationIntervalMinutes)
            return true;

        // Check message count
        if (state.MessageCount >= _options.MaxMessagesPerKey)
            return true;

        return false;
    }

    public ConnectionEncryptionStats? GetConnectionStats(string userId)
    {
        if (!_connections.TryGetValue(userId, out var state))
            return null;

        return new ConnectionEncryptionStats(
            state.KeyId,
            state.MessageCount,
            state.KeyCreatedAt,
            state.LastActivityAt
        );
    }

    public bool RemoveConnection(string userId)
    {
        if (_connections.TryRemove(userId, out var state))
        {
            // Securely clear key material
            CryptographicOperations.ZeroMemory(state.AesKey);
            if (state.PreviousAesKey != null)
                CryptographicOperations.ZeroMemory(state.PreviousAesKey);

            _logger.LogDebug("Removed encryption state for connection {userId}", userId);
            return true;
        }
        return false;
    }

    public IEnumerable<string> GetConnectionsNeedingRotation()
    {
        return _connections
            .Where(kvp => NeedsKeyRotation(kvp.Key))
            .Select(kvp => kvp.Key);
    }

    public IEnumerable<string> GetAllEncryptedUserIds()
    {
        return _connections.Keys.ToList();
    }

    public int CleanupExpiredPreviousKeys()
    {
        var cleanedCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _connections)
        {
            var state = kvp.Value;
            
            // Lock to synchronize with DecryptAndVerify - prevent zeroing key while in use
            lock (state.StateLock)
            {
                // Check if this connection has an expired previous key
                if (state.PreviousKeyExpiresAt.HasValue && 
                    now > state.PreviousKeyExpiresAt.Value &&
                    state.PreviousAesKey != null)
                {
                    // Securely clear the expired key
                    CryptographicOperations.ZeroMemory(state.PreviousAesKey);
                    state.PreviousAesKey = null;
                    state.PreviousKeyId = null;
                    state.PreviousKeyExpiresAt = null;
                    cleanedCount++;
                    
                    _logger.LogDebug("Cleaned up expired previous key for user {UserId}", kvp.Key);
                }
            }
        }

        if (cleanedCount > 0)
        {
            _metrics.AddExpiredKeysCleanedUp(cleanedCount);
            _logger.LogInformation("Cleaned up {Count} expired previous keys", cleanedCount);
        }

        return cleanedCount;
    }

    public EncryptionMetricsSnapshot GetMetrics() => _metrics.GetSnapshot();

    private bool _disposed;
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // Dispose cryptographic resources
        _serverSigningKey?.Dispose();
        _initLock.Dispose();
        
        // Securely clear all connection key material
        foreach (var kvp in _connections)
        {
            var state = kvp.Value;
            CryptographicOperations.ZeroMemory(state.AesKey);
            if (state.PreviousAesKey != null)
                CryptographicOperations.ZeroMemory(state.PreviousAesKey);
            if (state.PendingRotationEcdhPrivateKey != null)
                CryptographicOperations.ZeroMemory(state.PendingRotationEcdhPrivateKey);
        }
        _connections.Clear();
    }

    #region Private Helpers

    private static byte[] DeriveKey(byte[] sharedSecret, byte[] salt, string info, int keyLength)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret,
            keyLength, salt: salt, info: System.Text.Encoding.UTF8.GetBytes(info));
    }

    private static string GenerateKeyId()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>
    /// Computes a deterministic hash for nonce construction.
    /// Uses SHA256 truncated to 4 bytes instead of string.GetHashCode() 
    /// which is non-deterministic across processes/app domains.
    /// </summary>
    private static int ComputeConnectionHash(string userId)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(userId);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }

    private byte[] CreateSignature(string keyId, byte[] nonce, byte[] ciphertext, byte[] tag, long timestamp, long sequenceNumber)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(keyId);
        writer.Write(nonce);
        writer.Write(ciphertext);
        writer.Write(tag);
        writer.Write(timestamp);
        writer.Write(sequenceNumber);
        writer.Flush();

        return _serverSigningKey!.SignData(stream.ToArray(), HashAlgorithmName.SHA256);
    }

    private static bool VerifySignature(SecureEnvelope envelope, byte[] clientSigningPublicKey)
    {
        using var clientEcdsa = ECDsa.Create();
        clientEcdsa.ImportSubjectPublicKeyInfo(clientSigningPublicKey, out _);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(envelope.KeyId);
        writer.Write(envelope.Nonce);
        writer.Write(envelope.Ciphertext);
        writer.Write(envelope.Tag);
        writer.Write(envelope.Timestamp);
        writer.Write(envelope.SequenceNumber);
        writer.Flush();

        return clientEcdsa.VerifyData(stream.ToArray(), envelope.Signature, HashAlgorithmName.SHA256);
    }

    #endregion

    /// <summary>
    /// Internal state for a connection's encryption.
    /// </summary>
    private class ConnectionEncryptionState
    {
        public required string KeyId { get; set; }
        public required byte[] AesKey { get; set; }
        public required byte[] HkdfSalt { get; set; }
        public required byte[] ClientSigningPublicKey { get; set; }
        public required int UserIdHash { get; set; }
        public long NonceCounter;
        public long MessageCount;
        /// <summary>
        /// Per-keyId sequence tracking to support sequence number reset during key rotation.
        /// </summary>
        private readonly ConcurrentDictionary<string, long> _lastSequencePerKey = new();
        public long ServerSequenceNumber;
        public DateTimeOffset KeyCreatedAt { get; set; }
        
        // Use ticks for atomic updates via Interlocked
        private long _lastActivityAtTicks;
        public DateTimeOffset? LastActivityAt 
        {
            get => _lastActivityAtTicks == 0 ? null : new DateTimeOffset(_lastActivityAtTicks, TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastActivityAtTicks, value?.UtcTicks ?? 0);
        }

        // Previous key for grace period during rotation
        public string? PreviousKeyId { get; set; }
        public byte[]? PreviousAesKey { get; set; }
        public byte[]? PreviousClientSigningPublicKey { get; set; }
        public DateTimeOffset? PreviousKeyExpiresAt { get; set; }

        // Pending rotation state
        public string? PendingRotationKeyId { get; set; }
        public byte[]? PendingRotationEcdhPrivateKey { get; set; }
        public byte[]? PendingRotationHkdfSalt { get; set; }
        
        // Lock for rotation completion and key access to prevent TOCTOU races
        public readonly object StateLock = new();

        /// <summary>
        /// Gets the last received sequence number for a specific keyId.
        /// </summary>
        public long GetLastSequenceForKey(string keyId)
        {
            return _lastSequencePerKey.TryGetValue(keyId, out var seq) ? seq : 0;
        }

        /// <summary>
        /// Updates the last received sequence number for a specific keyId.
        /// </summary>
        public void SetLastSequenceForKey(string keyId, long sequenceNumber)
        {
            _lastSequencePerKey[keyId] = sequenceNumber;
        }

        /// <summary>
        /// Creates a ConnectionEncryptionState from a persisted state.
        /// </summary>
        public static ConnectionEncryptionState FromPersisted(PersistedEncryptionState persisted)
        {
            return new ConnectionEncryptionState
            {
                KeyId = persisted.KeyId,
                AesKey = persisted.AesKey,
                HkdfSalt = persisted.HkdfSalt,
                ClientSigningPublicKey = persisted.ClientSigningPublicKey,
                UserIdHash = persisted.UserIdHash,
                NonceCounter = persisted.NonceCounter,
                MessageCount = persisted.MessageCount,
                ServerSequenceNumber = persisted.ServerSequenceNumber,
                KeyCreatedAt = persisted.KeyCreatedAt,
                LastActivityAt = persisted.LastActivityAt,
                PreviousKeyId = persisted.PreviousKeyId,
                PreviousAesKey = persisted.PreviousAesKey,
                PreviousClientSigningPublicKey = persisted.PreviousClientSigningPublicKey,
                PreviousKeyExpiresAt = persisted.PreviousKeyExpiresAt
            };
        }

        /// <summary>
        /// Converts to a persisted state for Redis storage.
        /// </summary>
        public PersistedEncryptionState ToPersisted()
        {
            return new PersistedEncryptionState
            {
                KeyId = KeyId,
                AesKey = AesKey,
                HkdfSalt = HkdfSalt,
                ClientSigningPublicKey = ClientSigningPublicKey,
                UserIdHash = UserIdHash,
                NonceCounter = NonceCounter,
                MessageCount = MessageCount,
                LastSequenceNumber = 0, // Per-keyId tracking doesn't persist well, reset on restore
                ServerSequenceNumber = ServerSequenceNumber,
                KeyCreatedAt = KeyCreatedAt,
                LastActivityAt = LastActivityAt,
                PreviousKeyId = PreviousKeyId,
                PreviousAesKey = PreviousAesKey,
                PreviousClientSigningPublicKey = PreviousClientSigningPublicKey,
                PreviousKeyExpiresAt = PreviousKeyExpiresAt
            };
        }
    }
}
