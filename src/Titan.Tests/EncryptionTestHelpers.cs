using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Titan.Abstractions.Models;
using Titan.API.Config;
using Titan.API.Services.Encryption;

namespace Titan.Tests;

/// <summary>
/// Shared test helpers for encryption unit tests.
/// Reduces code duplication across EncryptionServiceTests and EncryptionSecurityTests.
/// </summary>
public static class EncryptionTestHelpers
{
    /// <summary>
    /// Creates a default EncryptionService for testing with standard options.
    /// </summary>
    public static EncryptionService CreateEncryptionService(EncryptionOptions? options = null)
    {
        var effectiveOptions = options ?? new EncryptionOptions
        {
            Enabled = true,
            RequireEncryption = false,
            KeyRotationIntervalMinutes = 30,
            MaxMessagesPerKey = 1000000,
            ReplayWindowSeconds = 60,
            KeyRotationGracePeriodSeconds = 30
        };

        return new EncryptionService(
            Options.Create(effectiveOptions),
            NullLogger<EncryptionService>.Instance,
            new EncryptionMetrics());
    }

    /// <summary>
    /// Creates a client SecureEnvelope for testing encryption/decryption.
    /// Performs AES-GCM encryption and creates a valid signature.
    /// </summary>
    /// <param name="plaintext">The plaintext to encrypt.</param>
    /// <param name="aesKey">The AES key for encryption.</param>
    /// <param name="keyId">The key ID to include in the envelope.</param>
    /// <param name="signingKey">The ECDsa key to sign the envelope.</param>
    /// <param name="sequenceNumber">Optional sequence number. Uses current ticks if not specified.</param>
    /// <param name="timestamp">Optional timestamp. Uses current time if not specified.</param>
    /// <returns>A properly formed SecureEnvelope.</returns>
    public static SecureEnvelope CreateClientEnvelope(
        byte[] plaintext,
        byte[] aesKey,
        string keyId,
        ECDsa signingKey,
        long? sequenceNumber = null,
        long? timestamp = null)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(aesKey, 16);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seqNum = sequenceNumber ?? DateTimeOffset.UtcNow.Ticks;

        // Create signature over all envelope fields
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(keyId);
        writer.Write(nonce);
        writer.Write(ciphertext);
        writer.Write(tag);
        writer.Write(ts);
        writer.Write(seqNum);
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
            SequenceNumber = seqNum
        };
    }

    /// <summary>
    /// Sets up an encryption connection for testing.
    /// Returns a disposable wrapper that manages the ECDsa signing key lifecycle.
    /// </summary>
    /// <param name="service">The encryption service to use.</param>
    /// <param name="connectionId">The connection ID to register.</param>
    /// <returns>A disposable EncryptionConnectionSetup containing all necessary test state.</returns>
    public static async Task<EncryptionConnectionSetup> SetupConnectionAsync(
        EncryptionService service,
        string connectionId)
    {
        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256); // Caller disposes via wrapper

        var response = await service.PerformKeyExchangeAsync(
            connectionId,
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
}

/// <summary>
/// Disposable wrapper for encryption connection state used in tests.
/// Automatically disposes the ECDsa signing key when disposed.
/// </summary>
public sealed class EncryptionConnectionSetup : IDisposable
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
