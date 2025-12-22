using System.Security.Cryptography;
using Titan.Abstractions.Models;
using Titan.Client.Encryption;

namespace Titan.Tests;

/// <summary>
/// Unit tests for ClientEncryptor - tests key exchange, encryption, key rotation,
/// sequence numbers, and proper disposal.
/// </summary>
public class ClientEncryptorTests
{
    [Fact]
    public async Task CanGenerateKeyPairsAndEncrypt()
    {
        // Arrange
        using var encryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act - Simulate key exchange
        var exchanged = await encryptor.PerformKeyExchangeAsync(request =>
        {
            Assert.NotEmpty(request.ClientSigningPublicKey.ToArray());
            return Task.FromResult(new KeyExchangeResponse(
                "test-key-id",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverSigningKey.ExportSubjectPublicKeyInfo(),
                new byte[32]  // HkdfSalt for testing
            ));
        });

        // Assert
        Assert.True(exchanged);
        Assert.True(encryptor.IsInitialized);
        Assert.Equal("test-key-id", encryptor.CurrentKeyId);

        // Test encryption works
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");
        var envelope = encryptor.EncryptAndSign(plaintext);

        Assert.NotNull(envelope);
        Assert.Equal("test-key-id", envelope.KeyId);
        Assert.Equal(12, envelope.Nonce.Length);
        Assert.Equal(16, envelope.Tag.Length);
        Assert.NotEmpty(envelope.Signature);
        Assert.True(envelope.SequenceNumber > 0);
    }

    [Fact]
    public async Task KeyRotation_GeneratesNewKeys()
    {
        // Arrange
        using var encryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await encryptor.PerformKeyExchangeAsync(_ => Task.FromResult(
            new KeyExchangeResponse(
                "initial-key",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverSigningKey.ExportSubjectPublicKeyInfo(),
                new byte[32])
        ));

        var initialKeyId = encryptor.CurrentKeyId;

        // Act - Key rotation
        using var newServerEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var rotationAck = encryptor.HandleRotationRequest(
            new KeyRotationRequest("rotated-key", newServerEcdh.ExportSubjectPublicKeyInfo(), new byte[32])
        );

        // Assert
        Assert.NotNull(rotationAck);
        Assert.NotEmpty(rotationAck.ClientPublicKey.ToArray());
        Assert.Equal("rotated-key", encryptor.CurrentKeyId);
        Assert.NotEqual(initialKeyId, encryptor.CurrentKeyId);
    }

    [Fact]
    public async Task SequenceNumbers_Increment()
    {
        // Arrange
        using var encryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await encryptor.PerformKeyExchangeAsync(_ => Task.FromResult(
            new KeyExchangeResponse(
                "seq-test-key",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverSigningKey.ExportSubjectPublicKeyInfo(),
                new byte[32])
        ));

        // Act - Encrypt multiple messages
        var envelope1 = encryptor.EncryptAndSign("Message 1"u8.ToArray());
        var envelope2 = encryptor.EncryptAndSign("Message 2"u8.ToArray());
        var envelope3 = encryptor.EncryptAndSign("Message 3"u8.ToArray());

        // Assert - Sequence numbers should increment
        Assert.Equal(1, envelope1.SequenceNumber);
        Assert.Equal(2, envelope2.SequenceNumber);
        Assert.Equal(3, envelope3.SequenceNumber);

        // Nonces should all be different
        Assert.NotEqual(envelope1.Nonce, envelope2.Nonce);
        Assert.NotEqual(envelope2.Nonce, envelope3.Nonce);
    }

    [Fact]
    public async Task Dispose_ClearsKeyMaterial()
    {
        // Arrange
        var encryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        await encryptor.PerformKeyExchangeAsync(_ => Task.FromResult(
            new KeyExchangeResponse(
                "dispose-test",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverSigningKey.ExportSubjectPublicKeyInfo(),
                new byte[32])
        ));

        Assert.True(encryptor.IsInitialized);

        // Act
        encryptor.Dispose();

        // Assert - After dispose, attempting to use should fail
        Assert.ThrowsAny<Exception>(() => encryptor.EncryptAndSign("test"u8.ToArray()));
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip()
    {
        // Arrange
        using var clientEncryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Client performs key exchange
        KeyExchangeRequest? capturedRequest = null;
        await clientEncryptor.PerformKeyExchangeAsync(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new KeyExchangeResponse(
                "roundtrip-key",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverEcdsa.ExportSubjectPublicKeyInfo(),
                new byte[32]
            ));
        });

        // Server derives same shared secret
        using var clientEcdh = ECDiffieHellman.Create();
        clientEcdh.ImportSubjectPublicKeyInfo(capturedRequest!.ClientPublicKey.Span, out _);
        var serverSharedSecret = serverEcdh.DeriveRawSecretAgreement(clientEcdh.PublicKey);

        // Derive AES key on server side (matching the salt provided in KeyExchangeResponse)
        var serverAesKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, serverSharedSecret, 32,
            salt: new byte[32],  // Same salt as in KeyExchangeResponse
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));

        // Act - Client encrypts a message
        var originalMessage = "Hello encrypted world!"u8.ToArray();
        var envelope = clientEncryptor.EncryptAndSign(originalMessage);

        // Server decrypts using derived key
        using var aesGcm = new AesGcm(serverAesKey, 16);
        var decrypted = new byte[envelope.Ciphertext.Length];
        aesGcm.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, decrypted);

        // Assert
        Assert.Equal(originalMessage, decrypted);
    }

    [Fact]
    public async Task GracePeriod_EncryptsWithNewKeyAfterRotation()
    {
        // Test that after rotation, messages use the new key and old key info is retained
        
        // Arrange
        using var encryptor = new ClientEncryptor();
        using var serverEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var serverEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Initial key exchange
        await encryptor.PerformKeyExchangeAsync(_ => Task.FromResult(
            new KeyExchangeResponse(
                "key-v1",
                serverEcdh.ExportSubjectPublicKeyInfo(),
                serverEcdsa.ExportSubjectPublicKeyInfo(),
                new byte[32])
        ));

        // Encrypt with key-v1
        var oldKeyMessage = encryptor.EncryptAndSign("Message with old key"u8.ToArray());
        Assert.Equal("key-v1", oldKeyMessage.KeyId);

        // Rotate to key-v2
        using var newServerEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        encryptor.HandleRotationRequest(
            new KeyRotationRequest("key-v2", newServerEcdh.ExportSubjectPublicKeyInfo(), new byte[32])
        );

        // Assert - Previous key is preserved during grace period
        Assert.Equal("key-v1", encryptor.PreviousKeyId);

        // Encrypt with key-v2
        var newKeyMessage = encryptor.EncryptAndSign("Message with new key"u8.ToArray());
        Assert.Equal("key-v2", newKeyMessage.KeyId);

        // Assert - Current key is v2
        Assert.Equal("key-v2", encryptor.CurrentKeyId);
    }
}
