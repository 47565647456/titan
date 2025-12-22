using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.Encryption;
using Xunit;

namespace Titan.Tests;

public class EncryptionSecurityTests
{
    private readonly EncryptionService _service;
    private readonly EncryptionOptions _options;

    public EncryptionSecurityTests()
    {
        _options = new EncryptionOptions
        {
            Enabled = true,
            RequireEncryption = true,
            ReplayWindowSeconds = 60
        };

        _service = new EncryptionService(
            Options.Create(_options),
            NullLogger<EncryptionService>.Instance,
            new EncryptionMetrics());
    }

    [Fact]
    public async Task Decrypt_WithExpiredTimestamp_ThrowsSecurityException()
    {
        // Arrange
        var connectionId = "expired-timestamp-user";
        using var setup = await SetupConnection(connectionId);
        
        // Create an envelope with an old timestamp (older than 60s)
        var oldTimestamp = DateTimeOffset.UtcNow.AddSeconds(-70).ToUnixTimeMilliseconds();
        var envelope = CreateClientEnvelope("Plaintext"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1, oldTimestamp);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope));
        Assert.Contains("timestamp outside valid window", ex.Message);
    }

    [Fact]
    public async Task Decrypt_WithFutureTimestamp_OutsideSkew_ThrowsSecurityException()
    {
        // Arrange
        var connectionId = "future-timestamp-user";
        using var setup = await SetupConnection(connectionId);
        
        // Create an envelope with a future timestamp (more than 5s in future)
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeMilliseconds();
        var envelope = CreateClientEnvelope("Plaintext"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1, futureTimestamp);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope));
        Assert.Contains("timestamp outside valid window", ex.Message);
    }

    [Fact]
    public async Task Decrypt_WithInvalidSignature_ThrowsSecurityException()
    {
        // Arrange
        var connectionId = "invalid-sig-user";
        using var setup = await SetupConnection(connectionId);
        
        var envelope = CreateClientEnvelope("Plaintext"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1);

        // Tamper with the signature
        envelope.Signature[0] ^= 0xFF;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope));
        Assert.Contains("Signature verification failed", ex.Message);
    }

    [Fact]
    public async Task Decrypt_WithWrongKeyId_ThrowsSecurityException()
    {
        // Arrange
        var connectionId = "wrong-keyid-user";
        using var setup = await SetupConnection(connectionId);
        
        var envelope = CreateClientEnvelope("Plaintext"u8.ToArray(), setup.AesKey, "invalid-key-id", setup.SigningKey, 1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope));
        Assert.Contains("Invalid key ID", ex.Message);
    }

    [Fact]
    public async Task Decrypt_WithReplayedSequenceNumber_ThrowsSecurityException()
    {
        // Arrange
        var connectionId = "replay-test-user";
        using var setup = await SetupConnection(connectionId);
        
        // First message with sequence 1 should succeed
        var envelope1 = CreateClientEnvelope("First message"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1);
        await _service.DecryptAndVerifyAsync(connectionId, envelope1);
        
        // Second message with same sequence 1 should fail (replay attack)
        var envelope2 = CreateClientEnvelope("Replayed message"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1);
        
        // Act & Assert
        var ex = await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope2));
        Assert.Contains("Sequence number", ex.Message);
    }

    #region Helpers

    /// <summary>
    /// Disposable wrapper for encryption connection state used in tests.
    /// Automatically disposes the ECDsa signing key when disposed.
    /// </summary>
    private sealed class EncryptionConnectionSetup : IDisposable
    {
        public KeyExchangeResponse Response { get; }
        public byte[] AesKey { get; }
        public ECDsa SigningKey { get; }
        
        public EncryptionConnectionSetup(KeyExchangeResponse response, byte[] aesKey, ECDsa signingKey)
        {
            Response = response;
            AesKey = aesKey;
            SigningKey = signingKey;
        }
        
        public void Dispose() => SigningKey.Dispose();
    }

    /// <summary>
    /// Sets up an encryption connection for testing.
    /// The returned wrapper implements IDisposable and will clean up the signing key.
    /// </summary>
    private async Task<EncryptionConnectionSetup> SetupConnection(string userId)
    {
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await _service.PerformKeyExchangeAsync(
            userId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.ToArray(), out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            salt: response.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        return new EncryptionConnectionSetup(response, aesKey, clientEcdsa);
    }

    private static SecureEnvelope CreateClientEnvelope(
        byte[] plaintext,
        byte[] aesKey,
        string keyId,
        ECDsa signingKey,
        long sequenceNumber,
        long? timestamp = null)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(aesKey, 16);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(keyId);
        writer.Write(nonce);
        writer.Write(ciphertext);
        writer.Write(tag);
        writer.Write(ts);
        writer.Write(sequenceNumber);
        writer.Flush();

        var signature = signingKey.SignData(stream.ToArray(), HashAlgorithmName.SHA256);

        return new SecureEnvelope
        {
            KeyId = keyId,
            Nonce = nonce,
            Ciphertext = ciphertext,
            Tag = tag,
            Signature = signature,
            Timestamp = ts,
            SequenceNumber = sequenceNumber
        };
    }

    #endregion
}
