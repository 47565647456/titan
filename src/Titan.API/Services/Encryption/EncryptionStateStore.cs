using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Security.Cryptography;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Redis-backed storage for encryption state persistence.
/// Handles persisting and loading server signing keys and per-user encryption state.
/// </summary>
public class EncryptionStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EncryptionStateStore> _logger;

    // Redis key prefixes
    private const string SigningKeyPrefix = "encryption:signing-key";
    private const string EncryptionStatePrefix = "encryption:state:";

    public EncryptionStateStore(
        [FromKeyedServices("encryption")] IConnectionMultiplexer redis,
        ILogger<EncryptionStateStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Tries to load the server signing key from Redis.
    /// Returns null if not found.
    /// </summary>
    public async Task<byte[]?> LoadSigningKeyAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var keyData = await db.StringGetAsync(SigningKeyPrefix);
            
            if (keyData.IsNullOrEmpty)
            {
                _logger.LogDebug("No signing key found in Redis");
                return null;
            }

            _logger.LogInformation("Loaded server signing key from Redis");
            return (byte[])keyData!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load signing key from Redis, will generate new key");
            return null;
        }
    }

    /// <summary>
    /// Saves the server signing key to Redis (persists indefinitely).
    /// </summary>
    public async Task SaveSigningKeyAsync(byte[] privateKey)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(SigningKeyPrefix, privateKey);
            _logger.LogInformation("Saved server signing key to Redis");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save signing key to Redis");
            throw;
        }
    }

    /// <summary>
    /// Saves encryption state for a user.
    /// </summary>
    public async Task SaveEncryptionStateAsync(string userId, PersistedEncryptionState state)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetStateKey(userId);
            var data = MemoryPackSerializer.Serialize(state);
            
            // Set expiry to 24 hours - if user doesn't reconnect, state expires
            await db.StringSetAsync(key, data, TimeSpan.FromHours(24));
            _logger.LogDebug("Saved encryption state for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save encryption state for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Loads encryption state for a user.
    /// </summary>
    public async Task<PersistedEncryptionState?> LoadEncryptionStateAsync(string userId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetStateKey(userId);
            var data = await db.StringGetAsync(key);

            if (data.IsNullOrEmpty)
            {
                return null;
            }

            var state = MemoryPackSerializer.Deserialize<PersistedEncryptionState>((byte[])data!);
            _logger.LogDebug("Loaded encryption state for user {UserId}", userId);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load encryption state for user {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Removes encryption state for a user.
    /// </summary>
    public async Task RemoveEncryptionStateAsync(string userId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetStateKey(userId);
            await db.KeyDeleteAsync(key);
            _logger.LogDebug("Removed encryption state for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove encryption state for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Loads all persisted encryption states (for startup recovery).
    /// </summary>
    public async Task<Dictionary<string, PersistedEncryptionState>> LoadAllEncryptionStatesAsync()
    {
        var states = new Dictionary<string, PersistedEncryptionState>();
        
        try
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            
            // Scan for all encryption state keys
            var pattern = $"{EncryptionStatePrefix}*";
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                try
                {
                    var data = await db.StringGetAsync(key);
                    if (!data.IsNullOrEmpty)
                    {
                        var state = MemoryPackSerializer.Deserialize<PersistedEncryptionState>((byte[])data!);
                        if (state != null)
                        {
                            var userId = key.ToString().Substring(EncryptionStatePrefix.Length);
                            states[userId] = state;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize encryption state for key {Key}", key);
                }
            }

            _logger.LogInformation("Loaded {Count} encryption states from Redis", states.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load all encryption states from Redis");
        }

        return states;
    }

    private static string GetStateKey(string userId) => $"{EncryptionStatePrefix}{userId}";
}

/// <summary>
/// Persisted encryption state for a user.
/// Contains only the data needed to resume encrypted communication after restart.
/// </summary>
[MemoryPackable]
public partial class PersistedEncryptionState
{
    public required string KeyId { get; init; }
    public required byte[] AesKey { get; init; }
    public required byte[] ClientSigningPublicKey { get; init; }
    public required int UserIdHash { get; init; }
    public required long NonceCounter { get; init; }
    public required long MessageCount { get; init; }
    public required long LastSequenceNumber { get; init; }
    public required long ServerSequenceNumber { get; init; }
    public required DateTimeOffset KeyCreatedAt { get; init; }
    public required DateTimeOffset? LastActivityAt { get; init; }
    
    // Previous key for grace period
    public string? PreviousKeyId { get; init; }
    public byte[]? PreviousAesKey { get; init; }
    public byte[]? PreviousClientSigningPublicKey { get; init; }
    public DateTimeOffset? PreviousKeyExpiresAt { get; init; }
}
