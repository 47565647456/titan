using System.Threading;

namespace Titan.API.Services.Encryption;

/// <summary>
/// Thread-safe metrics for encryption operations.
/// Tracks key exchanges, message encryption/decryption, rotations, and failures.
/// </summary>
public class EncryptionMetrics
{
    private long _keyExchangesPerformed;
    private long _messagesEncrypted;
    private long _messagesDecrypted;
    private long _keyRotationsTriggered;
    private long _keyRotationsCompleted;
    private long _encryptionFailures;
    private long _decryptionFailures;
    private long _expiredKeysCleanedUp;

    /// <summary>Number of successful key exchanges</summary>
    public long KeyExchangesPerformed => Interlocked.Read(ref _keyExchangesPerformed);
    
    /// <summary>Number of messages encrypted</summary>
    public long MessagesEncrypted => Interlocked.Read(ref _messagesEncrypted);
    
    /// <summary>Number of messages decrypted</summary>
    public long MessagesDecrypted => Interlocked.Read(ref _messagesDecrypted);
    
    /// <summary>Number of key rotations initiated</summary>
    public long KeyRotationsTriggered => Interlocked.Read(ref _keyRotationsTriggered);
    
    /// <summary>Number of key rotations completed</summary>
    public long KeyRotationsCompleted => Interlocked.Read(ref _keyRotationsCompleted);
    
    /// <summary>Number of encryption failures</summary>
    public long EncryptionFailures => Interlocked.Read(ref _encryptionFailures);
    
    /// <summary>Number of decryption failures</summary>
    public long DecryptionFailures => Interlocked.Read(ref _decryptionFailures);
    
    /// <summary>Number of expired keys cleaned up by background service</summary>
    public long ExpiredKeysCleanedUp => Interlocked.Read(ref _expiredKeysCleanedUp);

    public void IncrementKeyExchanges() => Interlocked.Increment(ref _keyExchangesPerformed);
    public void IncrementMessagesEncrypted() => Interlocked.Increment(ref _messagesEncrypted);
    public void IncrementMessagesDecrypted() => Interlocked.Increment(ref _messagesDecrypted);
    public void IncrementKeyRotationsTriggered() => Interlocked.Increment(ref _keyRotationsTriggered);
    public void IncrementKeyRotationsCompleted() => Interlocked.Increment(ref _keyRotationsCompleted);
    public void IncrementEncryptionFailures() => Interlocked.Increment(ref _encryptionFailures);
    public void IncrementDecryptionFailures() => Interlocked.Increment(ref _decryptionFailures);
    public void AddExpiredKeysCleanedUp(int count) => Interlocked.Add(ref _expiredKeysCleanedUp, count);

    /// <summary>
    /// Gets a snapshot of all metrics.
    /// </summary>
    public EncryptionMetricsSnapshot GetSnapshot() => new(
        KeyExchangesPerformed,
        MessagesEncrypted,
        MessagesDecrypted,
        KeyRotationsTriggered,
        KeyRotationsCompleted,
        EncryptionFailures,
        DecryptionFailures,
        ExpiredKeysCleanedUp
    );
}

/// <summary>
/// Immutable snapshot of encryption metrics at a point in time.
/// </summary>
public record EncryptionMetricsSnapshot(
    long KeyExchangesPerformed,
    long MessagesEncrypted,
    long MessagesDecrypted,
    long KeyRotationsTriggered,
    long KeyRotationsCompleted,
    long EncryptionFailures,
    long DecryptionFailures,
    long ExpiredKeysCleanedUp
);
