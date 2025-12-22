using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using Titan.Abstractions.Models;

namespace Titan.Client.Encryption;

/// <summary>
/// Client-side implementation of application-layer encryption using AES-256-GCM and ECDSA.
/// </summary>
public class ClientEncryptor : IClientEncryptor, IDisposable
{
    private const int ReplayWindowSeconds = 60;
    private const int ClockSkewSeconds = 5;
    
    private readonly ECDsa _signingKey;
    private readonly byte[] _signingPublicKey;
    private ECDiffieHellman? _currentEcdh;

    private string? _keyId;
    private byte[]? _aesKey;
    private byte[]? _serverSigningPublicKey;
    private int _connectionIdHash;
    private long _nonceCounter;
    private long _sequenceNumber;
    /// <summary>
    /// Tracks last received sequence number per keyId to prevent sequence regression after key rotation.
    /// Uses ConcurrentDictionary for thread-safety.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _lastServerSequencePerKey = new();

    // Previous key for grace period
    private string? _previousKeyId;
    private byte[]? _previousAesKey;
    private DateTimeOffset? _previousKeyExpiresAt;
    
    /// <summary>
    /// Grace period duration for previous keys during rotation.
    /// Should match server's KeyRotationGracePeriodSeconds.
    /// </summary>
    private const int GracePeriodSeconds = 300;

    public bool IsInitialized => _aesKey != null;
    public string? CurrentKeyId => _keyId;
    public string? PreviousKeyId => _previousKeyId;
    public byte[] SigningPublicKey => _signingPublicKey;

    public ClientEncryptor()
    {
        _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _signingPublicKey = _signingKey.ExportSubjectPublicKeyInfo();
    }

    public async Task<bool> PerformKeyExchangeAsync(Func<KeyExchangeRequest, Task<KeyExchangeResponse>> keyExchangeFunc)
    {
        // Generate ephemeral ECDH keypair
        _currentEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientPublicKey = _currentEcdh.ExportSubjectPublicKeyInfo();

        var request = new KeyExchangeRequest(clientPublicKey, _signingPublicKey);
        var response = await keyExchangeFunc(request);

        // Import server's public key and derive shared secret
        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.Span, out _);

        var sharedSecret = _currentEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);

        // Derive AES key from shared secret using HKDF with salt from server
        _aesKey = DeriveKey(sharedSecret, response.HkdfSalt.ToArray(), "titan-encryption-key", 32);
        
        // Zero out shared secret immediately after key derivation
        CryptographicOperations.ZeroMemory(sharedSecret);
        _keyId = response.KeyId;
        _serverSigningPublicKey = response.ServerSigningPublicKey.ToArray();

        // Generate connection ID hash from a random value for this session
        _connectionIdHash = RandomNumberGenerator.GetInt32(int.MaxValue);
        _nonceCounter = 0;
        _sequenceNumber = 0;
        _lastServerSequencePerKey.Clear();

        return true;
    }

    public SecureEnvelope EncryptAndSign(byte[] plaintext)
    {
        if (_aesKey == null || _keyId == null)
            throw new InvalidOperationException("Key exchange has not been completed");

        // Generate nonce: [4 bytes connection hash][8 bytes counter]
        var nonce = new byte[12];
        BitConverter.TryWriteBytes(nonce.AsSpan(0, 4), _connectionIdHash);
        BitConverter.TryWriteBytes(nonce.AsSpan(4, 8), Interlocked.Increment(ref _nonceCounter));

        // Encrypt
        using var aesGcm = new AesGcm(_aesKey, 16);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);

        // Sign
        var signature = CreateSignature(_keyId, nonce, ciphertext, tag, timestamp, sequenceNumber);

        return new SecureEnvelope
        {
            KeyId = _keyId,
            Nonce = nonce,
            Ciphertext = ciphertext,
            Tag = tag,
            Signature = signature,
            Timestamp = timestamp,
            SequenceNumber = sequenceNumber
        };
    }

    public byte[] DecryptAndVerify(SecureEnvelope envelope)
    {
        // Clear expired previous key material before decryption
        ClearExpiredPreviousKey();

        if (_serverSigningPublicKey == null)
            throw new InvalidOperationException("Key exchange has not been completed");

        // Validate key ID (support current and previous key during rotation)
        byte[]? aesKey = null;
        if (envelope.KeyId == _keyId)
            aesKey = _aesKey;
        else if (envelope.KeyId == _previousKeyId)
            aesKey = _previousAesKey;

        if (aesKey == null)
            throw new SecurityException($"Invalid key ID: {envelope.KeyId}");

        // Validate timestamp for replay protection (60 second window with 5 second clock skew tolerance)
        var messageTime = DateTimeOffset.FromUnixTimeMilliseconds(envelope.Timestamp);
        var age = DateTimeOffset.UtcNow - messageTime;
        if (age.TotalSeconds > ReplayWindowSeconds || age.TotalSeconds < -ClockSkewSeconds)
            throw new SecurityException($"Message timestamp outside valid window: {age.TotalSeconds}s");

        // Validate sequence number per keyId to handle key rotation correctly
        _lastServerSequencePerKey.TryGetValue(envelope.KeyId, out var lastSeqForKey);
        if (envelope.SequenceNumber <= lastSeqForKey)
            throw new SecurityException($"Sequence number regression for key {envelope.KeyId}: {envelope.SequenceNumber} <= {lastSeqForKey}");

        // Verify signature
        if (!VerifySignature(envelope, _serverSigningPublicKey))
            throw new SecurityException("Signature verification failed");

        // Decrypt
        using var aesGcm = new AesGcm(aesKey, 16);
        var plaintext = new byte[envelope.Ciphertext.Length];
        aesGcm.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext);

        // Update last received sequence for this keyId (thread-safe)
        _lastServerSequencePerKey.AddOrUpdate(
            envelope.KeyId,
            envelope.SequenceNumber,
            (key, existing) => Math.Max(existing, envelope.SequenceNumber));

        return plaintext;
    }

    public KeyRotationAck HandleRotationRequest(KeyRotationRequest request)
    {
        // Keep previous key for grace period with expiry tracking
        _previousKeyId = _keyId;
        _previousAesKey = _aesKey;
        _previousKeyExpiresAt = DateTimeOffset.UtcNow.AddSeconds(GracePeriodSeconds);

        // Generate new ECDH keypair
        _currentEcdh?.Dispose();
        _currentEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientPublicKey = _currentEcdh.ExportSubjectPublicKeyInfo();

        // Import server's new public key and derive new shared secret
        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(request.ServerPublicKey.Span, out _);

        // Create ephemeral keys for KDF compatibility if needed, or just use raw agreement
        var sharedSecret = _currentEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        _aesKey = DeriveKey(sharedSecret, request.HkdfSalt.ToArray(), "titan-encryption-key", 32);
        
        // Zero out shared secret immediately after key derivation
        CryptographicOperations.ZeroMemory(sharedSecret);
        _keyId = request.KeyId;

        // Reset nonce counter
        _nonceCounter = 0;

        return new KeyRotationAck(clientPublicKey, _signingPublicKey);
    }

    /// <summary>
    /// Clears the previous key if it has expired.
    /// Should be called periodically or before decryption.
    /// </summary>
    public void ClearExpiredPreviousKey()
    {
        if (_previousKeyExpiresAt.HasValue && DateTimeOffset.UtcNow > _previousKeyExpiresAt.Value)
        {
            if (_previousAesKey != null)
            {
                CryptographicOperations.ZeroMemory(_previousAesKey);
                _previousAesKey = null;
            }
            _previousKeyId = null;
            _previousKeyExpiresAt = null;
        }
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

        return _signingKey.SignData(stream.ToArray(), HashAlgorithmName.SHA256);
    }

    private static bool VerifySignature(SecureEnvelope envelope, byte[] serverSigningPublicKey)
    {
        using var serverEcdsa = ECDsa.Create();
        serverEcdsa.ImportSubjectPublicKeyInfo(serverSigningPublicKey, out _);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(envelope.KeyId);
        writer.Write(envelope.Nonce);
        writer.Write(envelope.Ciphertext);
        writer.Write(envelope.Tag);
        writer.Write(envelope.Timestamp);
        writer.Write(envelope.SequenceNumber);
        writer.Flush();

        return serverEcdsa.VerifyData(stream.ToArray(), envelope.Signature, HashAlgorithmName.SHA256);
    }

    private static byte[] DeriveKey(byte[] sharedSecret, byte[] salt, string info, int keyLength)
    {
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret,
            keyLength, salt: salt, info: System.Text.Encoding.UTF8.GetBytes(info));
    }

    public void Dispose()
    {
        _signingKey.Dispose();
        _currentEcdh?.Dispose();

        if (_aesKey != null)
            CryptographicOperations.ZeroMemory(_aesKey);
        if (_previousAesKey != null)
            CryptographicOperations.ZeroMemory(_previousAesKey);

        GC.SuppressFinalize(this);
    }
}
