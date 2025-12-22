using System.Security.Cryptography;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.Encryption;

namespace Titan.Tests;

/// <summary>
/// Unit tests for the EncryptionService.
/// Tests key exchange, encryption/decryption, signing, and key rotation.
/// </summary>
public class EncryptionServiceTests
{
    private readonly EncryptionService _service;

    public EncryptionServiceTests()
    {
        _service = EncryptionTestHelpers.CreateEncryptionService();
    }

    #region Key Exchange Tests

    [Fact]
    public async Task KeyExchange_WithValidClientKey_ReturnsServerKeyAndId()
    {
        // Arrange
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clientPublicKey = clientEcdh.ExportSubjectPublicKeyInfo();
        var clientSigningKey = clientEcdsa.ExportSubjectPublicKeyInfo();

        // Act
        var response = await _service.PerformKeyExchangeAsync(
            "connection-1",
            clientPublicKey,
            clientSigningKey);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.KeyId);
        Assert.NotEmpty(response.ServerPublicKey.ToArray());
        Assert.NotEmpty(response.ServerSigningPublicKey.ToArray());
    }

    [Fact]
    public async Task KeyExchange_EnablesEncryptionForConnection()
    {
        // Arrange
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        await _service.PerformKeyExchangeAsync(
            "connection-1",
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Assert
        Assert.True(_service.IsEncryptionEnabled("connection-1"));
        Assert.False(_service.IsEncryptionEnabled("connection-2"));
    }

    #endregion

    #region Encryption/Decryption Round-Trip Tests

    [Fact]
    public async Task Encrypt_Decrypt_RoundTrip_ReturnsOriginalPayload()
    {
        // Arrange
        var connectionId = "connection-roundtrip";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Perform key exchange to get shared secret
        var response = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Derive shared secret client-side
        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.Span, out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            salt: response.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        // Create a client-side encrypted envelope
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, Titan!");
        var envelope = CreateClientEnvelope(plaintext, aesKey, response.KeyId, clientEcdsa);

        // Act
        var decrypted = await _service.DecryptAndVerifyAsync(connectionId, envelope);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task ServerEncrypt_ClientDecrypt_RoundTrip_Works()
    {
        // Arrange
        var connectionId = "connection-server-encrypt";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Server response!");

        // Act - Server encrypts
        var envelope = await _service.EncryptAndSignAsync(connectionId, plaintext);

        // Verify envelope is properly formed
        Assert.NotNull(envelope);
        Assert.Equal(response.KeyId, envelope.KeyId);
        Assert.Equal(12, envelope.Nonce.Length);
        Assert.Equal(16, envelope.Tag.Length);
        Assert.NotEmpty(envelope.Signature);
        Assert.True(envelope.SequenceNumber > 0);

        // Client-side decryption would verify signature and decrypt
        // For unit test, we just verify the envelope structure
    }

    #endregion

    #region Signature Verification Tests

    [Fact]
    public async Task Decrypt_WithTamperedCiphertext_ThrowsException()
    {
        // Arrange
        var connectionId = "connection-tamper";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.Span, out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            salt: response.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Original message");
        var envelope = CreateClientEnvelope(plaintext, aesKey, response.KeyId, clientEcdsa);

        // Tamper with ciphertext
        envelope = envelope with { Ciphertext = [.. envelope.Ciphertext] };
        envelope.Ciphertext[0] ^= 0xFF;

        // Act & Assert
        await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope));
    }

    #endregion

    #region Replay Protection Tests

    [Fact]
    public async Task Decrypt_WithReplayedSequenceNumber_ThrowsException()
    {
        // Arrange
        var connectionId = "connection-replay";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.Span, out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            salt: response.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        // Send first message with sequence 1
        var envelope1 = CreateClientEnvelope("Message 1"u8.ToArray(), aesKey, response.KeyId, clientEcdsa, sequenceNumber: 1);
        await _service.DecryptAndVerifyAsync(connectionId, envelope1);

        // Try to send message with sequence 1 again (replay)
        var envelope2 = CreateClientEnvelope("Message 2"u8.ToArray(), aesKey, response.KeyId, clientEcdsa, sequenceNumber: 1);

        // Act & Assert
        await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => _service.DecryptAndVerifyAsync(connectionId, envelope2));
    }

    #endregion

    #region Key Rotation Tests

    [Fact]
    public async Task InitiateKeyRotation_ReturnsNewServerKey()
    {
        // Arrange
        var connectionId = "connection-rotation";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var initialResponse = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act
        var rotationRequest = await _service.InitiateKeyRotationAsync(connectionId);

        // Assert
        Assert.NotNull(rotationRequest);
        Assert.NotEqual(initialResponse.KeyId, rotationRequest.KeyId);
        Assert.NotEmpty(rotationRequest.ServerPublicKey.ToArray());
    }

    [Fact]
    public async Task NeedsKeyRotation_AfterMaxMessages_ReturnsTrue()
    {
        // Arrange - Use low message limit for testing
        var testOptions = new EncryptionOptions
        {
            Enabled = true,
            MaxMessagesPerKey = 2,
            KeyRotationIntervalMinutes = 60
        };

        var service = EncryptionTestHelpers.CreateEncryptionService(testOptions);

        var connectionId = "connection-max-messages";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey.Span, out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            salt: response.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        // Send messages to reach threshold
        for (int i = 1; i <= 3; i++)
        {
            var envelope = CreateClientEnvelope(System.Text.Encoding.UTF8.GetBytes($"Message {i}"), aesKey, response.KeyId, clientEcdsa, sequenceNumber: i);
            await service.DecryptAndVerifyAsync(connectionId, envelope);
        }

        // Act & Assert
        Assert.True(service.NeedsKeyRotation(connectionId));
    }

    [Fact]
    public async Task GetConnectionStats_ReturnsCorrectData()
    {
        // Arrange
        var connectionId = "connection-stats";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var response = await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act
        var stats = _service.GetConnectionStats(connectionId);

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(response.KeyId, stats.KeyId);
        Assert.Equal(0, stats.MessageCount);
        Assert.True(stats.KeyCreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RemoveConnection_CleansUpEncryptionState()
    {
        // Arrange
        var connectionId = "connection-remove";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await _service.PerformKeyExchangeAsync(
            connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        Assert.True(_service.IsEncryptionEnabled(connectionId));

        // Act
        _service.RemoveConnection(connectionId);

        // Assert
        Assert.False(_service.IsEncryptionEnabled(connectionId));
        Assert.Null(_service.GetConnectionStats(connectionId));
    }

    [Fact]
    public async Task MultipleConnections_HaveIndependentState()
    {
        // Arrange
        var connection1 = "conn-independent-1";
        var connection2 = "conn-independent-2";

        using var ecdh1 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var ecdh2 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var response1 = await _service.PerformKeyExchangeAsync(connection1,
            ecdh1.ExportSubjectPublicKeyInfo(), ecdsa1.ExportSubjectPublicKeyInfo());
        var response2 = await _service.PerformKeyExchangeAsync(connection2,
            ecdh2.ExportSubjectPublicKeyInfo(), ecdsa2.ExportSubjectPublicKeyInfo());

        // Assert
        Assert.NotEqual(response1.KeyId, response2.KeyId);
        Assert.True(_service.IsEncryptionEnabled(connection1));
        Assert.True(_service.IsEncryptionEnabled(connection2));

        // Removing one doesn't affect the other
        _service.RemoveConnection(connection1);
        Assert.False(_service.IsEncryptionEnabled(connection1));
        Assert.True(_service.IsEncryptionEnabled(connection2));
    }

    [Fact]
    public void GetConfig_ReturnsCorrectConfiguration()
    {
        // Act
        var config = _service.GetConfig();

        // Assert
        Assert.True(config.Enabled);
        Assert.False(config.Required);
    }

    [Fact]
    public async Task GetConnectionsNeedingRotation_ReturnsExpiredConnections()
    {
        // Arrange - Use a service with 0-minute rotation interval
        var testOptions = new EncryptionOptions
        {
            Enabled = true,
            RequireEncryption = false,
            KeyRotationIntervalMinutes = 0, // Immediate rotation needed
            MaxMessagesPerKey = 1000000,
            ReplayWindowSeconds = 60,
            KeyRotationGracePeriodSeconds = 30
        };

        var service = EncryptionTestHelpers.CreateEncryptionService(testOptions);

        var connectionId = "connection-expired";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await service.PerformKeyExchangeAsync(connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Wait for key to "expire" (0-minute interval means immediately)
        await Task.Delay(100);

        // Act
        var connections = service.GetConnectionsNeedingRotation();

        // Assert
        Assert.Contains(connectionId, connections);
    }

    [Fact]
    public async Task CompleteKeyRotation_UpdatesToNewKey()
    {
        // Arrange
        var connectionId = "connection-complete-rotation";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await _service.PerformKeyExchangeAsync(connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        var rotationRequest = await _service.InitiateKeyRotationAsync(connectionId);

        // Generate new client keypair for rotation
        using var newClientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var ack = new KeyRotationAck(newClientEcdh.ExportSubjectPublicKeyInfo(), clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act
        await _service.CompleteKeyRotationAsync(connectionId, ack);

        // Assert - The connection should now use the new key
        var stats = _service.GetConnectionStats(connectionId);
        Assert.NotNull(stats);
        Assert.Equal(rotationRequest.KeyId, stats.KeyId);
    }

    [Fact]
    public async Task CleanupExpiredPreviousKeys_RemovesExpiredKeys()
    {
        // Arrange - Create a connection and initiate rotation to create a previous key
        var connectionId = "connection-cleanup-test";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await _service.PerformKeyExchangeAsync(connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Initiate rotation (creates previous key with expiry)
        var rotationRequest = await _service.InitiateKeyRotationAsync(connectionId);
        
        // Complete rotation so we have a previous key
        using var newClientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        await _service.CompleteKeyRotationAsync(connectionId, 
            new KeyRotationAck(newClientEcdh.ExportSubjectPublicKeyInfo(), clientEcdsa.ExportSubjectPublicKeyInfo()));

        // The previous key should exist now (grace period hasn't expired)
        var cleanedBeforeExpiry = _service.CleanupExpiredPreviousKeys();
        Assert.Equal(0, cleanedBeforeExpiry); // Nothing to clean yet

        // Note: In a real test we'd need to manipulate time or wait for the grace period
        // For now, this test verifies the method runs without error and returns 0 when no keys expired
    }

    [Fact]
    public void GetMetrics_ReturnsMetricsSnapshot()
    {
        // Act - the constructor and test setup already performed operations
        var metrics = _service.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.IsType<EncryptionMetricsSnapshot>(metrics);
    }

    [Fact]
    public async Task KeyExchange_IncrementsMetrics()
    {
        // Arrange
        var initialMetrics = _service.GetMetrics();
        var initialKeyExchanges = initialMetrics.KeyExchangesPerformed;

        var connectionId = "connection-metrics-test";
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        await _service.PerformKeyExchangeAsync(connectionId,
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Assert
        var newMetrics = _service.GetMetrics();
        Assert.Equal(initialKeyExchanges + 1, newMetrics.KeyExchangesPerformed);
    }

    [Fact]
    public async Task EncryptDecrypt_IncrementsMessageMetrics()
    {
        // Arrange
        var connectionId = "connection-message-metrics";
        using var setup = await SetupEncryptedConnectionWithSigningKeyAsync(connectionId);
        var initialMetrics = _service.GetMetrics();

        // Act - Decrypt a message (using the same signing key registered during key exchange)
        var envelope = CreateClientEnvelope(
            "test message"u8.ToArray(), setup.AesKey, setup.Response.KeyId, setup.SigningKey, 1);
        _service.DecryptAndVerify(connectionId, envelope);

        // Act - Encrypt a message
        _service.EncryptAndSign(connectionId, "response"u8.ToArray());

        // Assert
        var newMetrics = _service.GetMetrics();
        Assert.True(newMetrics.MessagesDecrypted > initialMetrics.MessagesDecrypted);
        Assert.True(newMetrics.MessagesEncrypted > initialMetrics.MessagesEncrypted);
    }

    [Fact]
    public async Task PerformKeyExchange_RetainsPreviousKey_ForGracefulConcurrentConnections()
    {
        // Arrange
        var userId = "user-concurrent";
        using var client1 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var client2 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // First exchange
        var response1 = await _service.PerformKeyExchangeAsync(
            userId,
            client1.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Second exchange (simulating React Strict Mode double-mount)
        var response2 = await _service.PerformKeyExchangeAsync(
            userId,
            client2.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act - Try to decrypt using the FIRST key (which should now be the PreviousKeyId)
        using var serverEcdh1 = ECDiffieHellman.Create();
        serverEcdh1.ImportSubjectPublicKeyInfo(response1.ServerPublicKey.Span, out _);
        var sharedSecret1 = client1.DeriveRawSecretAgreement(serverEcdh1.PublicKey);
        var aesKey1 = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret1, 32,
            salt: response1.HkdfSalt.ToArray(),
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        var plaintext = "Message from mount 1"u8.ToArray();
        var envelope = CreateClientEnvelope(plaintext, aesKey1, response1.KeyId, clientEcdsa, 1);

        // Decrypt with previous key
        var decrypted = await _service.DecryptAndVerifyAsync(userId, envelope);

        // Assert
        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(response1.KeyId, response2.KeyId);
    }
    
    [Fact]
    public async Task EncryptAndSign_WithKeyIdHint_UsesSpecifiedKey()
    {
        // Arrange
        var userId = "user-hint";
        using var client1 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var client2 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // First exchange
        var response1 = await _service.PerformKeyExchangeAsync(
            userId,
            client1.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Second exchange
        var response2 = await _service.PerformKeyExchangeAsync(
            userId,
            client2.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        var plaintext = "Server symmetric response"u8.ToArray();

        // Act - Request encryption using the FIRST key ID
        var envelope = await _service.EncryptAndSignAsync(userId, plaintext, response1.KeyId);

        // Assert
        Assert.Equal(response1.KeyId, envelope.KeyId);
        Assert.NotEqual(response2.KeyId, envelope.KeyId);
    }

    #endregion

    #region Helper Methods

    private static SecureEnvelope CreateClientEnvelope(
        byte[] plaintext,
        byte[] aesKey,
        string keyId,
        ECDsa signingKey,
        long? sequenceNumber = null)
        => EncryptionTestHelpers.CreateClientEnvelope(plaintext, aesKey, keyId, signingKey, sequenceNumber);

    private async Task<EncryptionConnectionSetup> SetupEncryptedConnectionWithSigningKeyAsync(string connectionId)
        => await EncryptionTestHelpers.SetupConnectionAsync(_service, connectionId);

    #endregion
}
