using System.Security.Cryptography;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.Encryption;
using Xunit;

namespace Titan.Tests;

public class EncryptionSecurityTests
{
    private readonly EncryptionService _service;

    public EncryptionSecurityTests()
    {
        _service = EncryptionTestHelpers.CreateEncryptionService(new EncryptionOptions
        {
            Enabled = true,
            RequireEncryption = true,
            ReplayWindowSeconds = 60
        });
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

    #region Helpers - Delegating to shared helpers

    private Task<EncryptionConnectionSetup> SetupConnection(string userId)
        => EncryptionTestHelpers.SetupConnectionAsync(_service, userId);

    private static SecureEnvelope CreateClientEnvelope(
        byte[] plaintext,
        byte[] aesKey,
        string keyId,
        ECDsa signingKey,
        long sequenceNumber,
        long? timestamp = null)
        => EncryptionTestHelpers.CreateClientEnvelope(plaintext, aesKey, keyId, signingKey, sequenceNumber, timestamp);

    #endregion
}
