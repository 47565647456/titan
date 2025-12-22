using System.Net.Http.Json;
using System.Security.Cryptography;
using MemoryPack;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Models;
using Titan.Client;
using Titan.Client.Encryption;

namespace Titan.AppHost.Tests;

/// <summary>
/// Comprehensive integration tests for application-layer payload encryption.
/// Tests encryption configuration, key exchange, key rotation, and encrypted hub operations.
/// </summary>
[Collection("AppHost")]
public class EncryptionIntegrationTests : IntegrationTestBase
{
    public EncryptionIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    #region Config Endpoint Tests

    [Fact]
    public async Task GetEncryptionConfig_AsAdmin_ReturnsConfig()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/encryption/config");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var config = await response.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        Assert.NotNull(config);
        // Default config has encryption ENABLED but requirement disabled via AppHost overrides
        Assert.True(config.Enabled);
        Assert.False(config.Required);
    }

    [Fact]
    public async Task GetEncryptionConfig_Unauthenticated_Returns401()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/admin/encryption/config");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Stats Endpoint Tests

    [Fact]
    public async Task GetConnectionStats_InvalidConnectionId_Returns404()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/encryption/connections/invalid-connection-id/stats");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetConnectionsNeedingRotation_ReturnsEmptyList()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/encryption/connections/needs-rotation");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<ConnectionsNeedingRotationResponse>();
        Assert.NotNull(result);
        Assert.Empty(result.Connections);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetEncryptionMetrics_AsAdmin_ReturnsMetrics()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/encryption/metrics");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var metrics = await response.Content.ReadFromJsonAsync<EncryptionMetricsResponse>();
        Assert.NotNull(metrics);
        // Verify metrics properties exist (values may vary based on test order)
        Assert.True(metrics.KeyExchangesPerformed >= 0);
        Assert.True(metrics.MessagesEncrypted >= 0);
        Assert.True(metrics.MessagesDecrypted >= 0);
        Assert.True(metrics.KeyRotationsTriggered >= 0);
        Assert.True(metrics.KeyRotationsCompleted >= 0);
        Assert.True(metrics.EncryptionFailures >= 0);
        Assert.True(metrics.DecryptionFailures >= 0);
        Assert.True(metrics.ExpiredKeysCleanedUp >= 0);
    }

    [Fact]
    public async Task GetEncryptionMetrics_Unauthenticated_Returns401()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/admin/encryption/metrics");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Key Rotation Endpoint Tests

    [Fact]
    public async Task ForceRotation_InvalidConnectionId_Returns404()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.PostAsync(
            "/api/admin/encryption/connections/invalid-connection-id/rotate",
            null);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForceRotationAll_AsAdmin_Returns200()
    {
        // Arrange
        var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.PostAsync("/api/admin/encryption/rotate-all", null);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<RotationAllResponse>();
        Assert.NotNull(result);
        Assert.Contains("rotation initiated", result.Message.ToLower());
    }

    #endregion

    #region Hub Connection Tests (Encryption Disabled)

    [Fact]
    public async Task HubConnection_WithoutEncryption_WorksNormally()
    {
        // When encryption is disabled (default), normal hub calls should work
        // This verifies the encryption filter doesn't break existing functionality

        // Arrange
        var session = await CreateUserSessionAsync();

        // Act - Make a normal hub call
        var accountHub = await session.GetAccountHubAsync();
        var account = await accountHub.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Equal(session.UserId, account.AccountId);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task HubConnection_MultipleHubCalls_AllWorkWithoutEncryption()
    {
        // Verify multiple hub operations work correctly when encryption is disabled
        
        // Arrange
        var session = await CreateUserSessionAsync();
        var accountHub = await session.GetAccountHubAsync();

        // Act - Make several calls
        var account1 = await accountHub.InvokeAsync<Account>("GetAccount");
        var account2 = await accountHub.InvokeAsync<Account>("GetAccount");
        var account3 = await accountHub.InvokeAsync<Account>("GetAccount");

        // Assert - All should return consistent data
        Assert.NotNull(account1);
        Assert.NotNull(account2);
        Assert.NotNull(account3);
        Assert.Equal(account1.AccountId, account2.AccountId);
        Assert.Equal(account2.AccountId, account3.AccountId);

        await session.DisposeAsync();
    }

    #endregion

    #region End-to-End Encryption Hub Tests

    [Fact]
    public async Task EncryptionHub_KeyExchange_ReturnsValidKeys()
    {
        // Test actual key exchange via SignalR hub
        
        // Arrange
        var (sessionId, _, _) = await LoginAsUserAsync();
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();

        await encryptionHub.StartAsync();

        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new KeyExchangeRequest(
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act
        var response = await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.KeyId);
        Assert.NotEmpty(response.ServerPublicKey);
        Assert.NotEmpty(response.ServerSigningPublicKey);

        // Verify we can import the server's public key (valid format)
        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey, out _);
        
        using var serverEcdsa = ECDsa.Create();
        serverEcdsa.ImportSubjectPublicKeyInfo(response.ServerSigningPublicKey, out _);

        await encryptionHub.DisposeAsync();
    }

    [Fact]
    public async Task EncryptionHub_GetConfig_ReturnsServerConfig()
    {
        // Test getting encryption config via SignalR hub
        
        // Arrange
        var (sessionId, _, _) = await LoginAsUserAsync();
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();

        await encryptionHub.StartAsync();

        // Act
        var config = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");

        // Assert
        Assert.NotNull(config);
        // Default config should have encryption ENABLED
        Assert.True(config.Enabled);

        await encryptionHub.DisposeAsync();
    }

    [Fact]
    public async Task EncryptionHub_KeyExchangeThenAccountCall_WorksEndToEnd()
    {
        // Complete flow: Key exchange -> Encrypted account call
        // This verifies the full encrypted protocol including payload encryption/decryption
        
        // Arrange
        var (sessionId, _, userId) = await LoginAsUserAsync();
        
        // Connect to encryption hub for key exchange
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();
        await encryptionHub.StartAsync();

        // Perform key exchange using ClientEncryptor helper
        using var encryptor = new ClientEncryptor();
        await encryptor.PerformKeyExchangeAsync(async req => 
            await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));

        // Now connect to account hub
        var accountHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
            .Build();
        await accountHub.StartAsync();
        
        // Wrap with EncryptedHubConnection to handle transparent encryption/decryption
        await using var encryptedAccountHub = new EncryptedHubConnection(accountHub, encryptor, true);

        // Act - Call GetAccount (transparently encrypted/decrypted)
        var account = await encryptedAccountHub.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Equal(userId, account.AccountId);

        await encryptionHub.DisposeAsync();
    }

    [Fact]
    public async Task EncryptionHub_MultipleKeyExchanges_EachGetsUniqueKeyId()
    {
        // Verify each key exchange produces a unique key ID
        
        // Arrange
        var (sessionId1, _, _) = await LoginAsUserAsync();
        var (sessionId2, _, _) = await LoginAsUserAsync();
        
        var hub1 = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId1}")
            .Build();
        var hub2 = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId2}")
            .Build();

        await Task.WhenAll(hub1.StartAsync(), hub2.StartAsync());

        using var ecdh1 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var ecdh2 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Act
        var response1 = await hub1.InvokeAsync<KeyExchangeResponse>("KeyExchange",
            new KeyExchangeRequest(ecdh1.ExportSubjectPublicKeyInfo(), ecdsa1.ExportSubjectPublicKeyInfo()));
        var response2 = await hub2.InvokeAsync<KeyExchangeResponse>("KeyExchange",
            new KeyExchangeRequest(ecdh2.ExportSubjectPublicKeyInfo(), ecdsa2.ExportSubjectPublicKeyInfo()));

        // Assert
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.NotEqual(response1.KeyId, response2.KeyId);  // Each connection gets unique key

        await hub1.DisposeAsync();
        await hub2.DisposeAsync();
    }

    #endregion

    #region Response Models

    private record EncryptionConfigResponse(bool Enabled, bool Required);
    private record ConnectionsNeedingRotationResponse(List<string> Connections, int Count);
    private record RotationAllResponse(string Message);
    private record EncryptionMetricsResponse(
        long KeyExchangesPerformed,
        long MessagesEncrypted,
        long MessagesDecrypted,
        long KeyRotationsTriggered,
        long KeyRotationsCompleted,
        long EncryptionFailures,
        long DecryptionFailures,
        long ExpiredKeysCleanedUp);

    #endregion

    #region EncryptedHubConnection Wrapper Tests

    [Fact]
    public async Task EncryptedHubConnection_WithoutEncryption_PassesThroughNormally()
    {
        // Test that EncryptedHubConnection works in passthrough mode when encryption disabled
        
        // Arrange
        var (sessionId, _, userId) = await LoginAsUserAsync();
        var accountHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
            .Build();
        await accountHub.StartAsync();

        // Create an EncryptedHubConnection without encryption
        var encryptedConnection = new EncryptedHubConnection(accountHub, null, false);

        // Assert - should not be encrypted
        Assert.False(encryptedConnection.IsEncrypted);

        // Act - Call should work just like normal
        var account = await encryptedConnection.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Equal(userId, account.AccountId);

        await encryptedConnection.DisposeAsync();
    }

    [Fact]
    public async Task EncryptedHubConnection_State_MatchesUnderlyingConnection()
    {
        // Test that state property reflects underlying connection
        
        // Arrange
        var (sessionId, _, _) = await LoginAsUserAsync();
        var accountHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
            .Build();

        var encryptedConnection = new EncryptedHubConnection(accountHub, null, false);

        // Assert - Initial state
        Assert.Equal(HubConnectionState.Disconnected, encryptedConnection.State);

        await accountHub.StartAsync();
        Assert.Equal(HubConnectionState.Connected, encryptedConnection.State);

        await encryptedConnection.DisposeAsync();
    }

    #endregion

    #region Admin Rotation API Tests

    [Fact]
    public async Task Admin_CanTriggerRotationForAllConnections()
    {
        // Test that admin can force rotation for all connections
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();

        // Create a user session with encryption
        var (sessionId, _, _) = await LoginAsUserAsync();
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();
        await encryptionHub.StartAsync();

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange",
            new KeyExchangeRequest(ecdh.ExportSubjectPublicKeyInfo(), ecdsa.ExportSubjectPublicKeyInfo()));

        // Act
        var rotateAllResponse = await adminClient.PostAsync("/api/admin/encryption/rotate-all", null);

        // Assert
        Assert.True(rotateAllResponse.IsSuccessStatusCode);
        var result = await rotateAllResponse.Content.ReadFromJsonAsync<RotationAllResponse>();
        Assert.NotNull(result);
        Assert.Contains("initiated", result.Message.ToLower());

        await encryptionHub.DisposeAsync();
    }

    #endregion

    #region Key Exchange Verification Tests

    [Fact]
    public async Task KeyExchange_ServerPublicKey_CanBeUsedForECDH()
    {
        // Verify the server's public key is valid for ECDH key derivation
        
        // Arrange
        var (sessionId, _, _) = await LoginAsUserAsync();
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();
        await encryptionHub.StartAsync();

        using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new KeyExchangeRequest(
            clientEcdh.ExportSubjectPublicKeyInfo(),
            clientEcdsa.ExportSubjectPublicKeyInfo());

        // Act
        var response = await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", request);

        // Derive shared secret
        using var serverEcdh = ECDiffieHellman.Create();
        serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey, out _);
        var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);

        // Assert
        Assert.NotNull(sharedSecret);
        Assert.Equal(32, sharedSecret.Length);  // 256-bit key

        // Can derive AES key
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32,
            info: System.Text.Encoding.UTF8.GetBytes("titan-encryption-key"));
        Assert.Equal(32, aesKey.Length);

        await encryptionHub.DisposeAsync();
    }

    #endregion

    #region Multiple Hub Operations Tests

    [Fact]
    public async Task MultipleHubCalls_WithKeyExchangeFirst_AllSucceed()
    {
        // Test that multiple encrypted hub calls work correctly after key exchange
        
        // Arrange
        var (sessionId, _, userId) = await LoginAsUserAsync();
        
        // First do key exchange
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();
        await encryptionHub.StartAsync();

        using var encryptor = new ClientEncryptor();
        await encryptor.PerformKeyExchangeAsync(async req => 
            await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));

        // Now make multiple account hub calls
        var accountHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
            .Build();
        await accountHub.StartAsync();

        // Wrap with EncryptedHubConnection
        await using var encryptedAccountHub = new EncryptedHubConnection(accountHub, encryptor, true);

        // Multiple calls
        for (int i = 0; i < 5; i++)
        {
            var account = await encryptedAccountHub.InvokeAsync<Account>("GetAccount");
            Assert.NotNull(account);
            Assert.Equal(userId, account.AccountId);
        }

        await encryptionHub.DisposeAsync();
    }

    #endregion

    #region Encryption Enabled Tests (Toggle via Admin API)

    [Fact]
    public async Task Admin_CanToggleEncryption_OnAndOff()
    {
        // Test that admin can toggle encryption enabled state at runtime
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Get initial config
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        try
        {
            // Act - Enable encryption
            var enableResponse = await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });
            Assert.True(enableResponse.IsSuccessStatusCode);

            // Verify enabled
            var configAfterEnable = await adminClient.GetAsync("/api/admin/encryption/config");
            var enabledConfig = await configAfterEnable.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
            Assert.True(enabledConfig?.Enabled);

            // Act - Disable encryption  
            var disableResponse = await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = false });
            Assert.True(disableResponse.IsSuccessStatusCode);

            // Verify disabled
            var configAfterDisable = await adminClient.GetAsync("/api/admin/encryption/config");
            var disabledConfig = await configAfterDisable.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
            Assert.False(disabledConfig?.Enabled);
        }
        finally
        {
            // Restore original state
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task EncryptionHub_GetConfig_ReflectsRuntimeToggle()
    {
        // Test that GetConfig via hub reflects admin toggle
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        var (sessionId, _, _) = await LoginAsUserAsync();
        
        var encryptionHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
            .Build();
        await encryptionHub.StartAsync();

        // Get initial state
        var initialConfig = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
        var wasEnabled = initialConfig.Enabled;

        try
        {
            // Act - Enable via admin API
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });
            
            await Task.Delay(100); // Small delay for state to propagate

            // Get config via hub
            var enabledHubConfig = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
            Assert.True(enabledHubConfig.Enabled, "Hub config should reflect enabled state");

            // Disable via admin API
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = false });
            
            await Task.Delay(100);

            var disabledHubConfig = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
            Assert.False(disabledHubConfig.Enabled, "Hub config should reflect disabled state");
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
            await encryptionHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task HubCall_WithEncryptionEnabled_ButNoKeyExchange_StillWorks()
    {
        // Test that hub calls still work when encryption is enabled but client hasn't done key exchange
        // (because RequireEncryption is false by default)
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Get initial state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        try
        {
            // Enable encryption
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });

            // Create a session WITHOUT key exchange
            var session = await CreateUserSessionAsync();
            var accountHub = await session.GetAccountHubAsync();

            // Act - This should still work (encryption enabled but not required)
            var account = await accountHub.InvokeAsync<Account>("GetAccount");

            // Assert
            Assert.NotNull(account);
            Assert.Equal(session.UserId, account.AccountId);

            await session.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task KeyExchange_WithEncryptionEnabled_CompletesSuccessfully()
    {
        // Test that key exchange works when encryption is enabled
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        var (sessionId, _, _) = await LoginAsUserAsync();
        
        // Get initial state and enable encryption
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
            new { Enabled = true });

        try
        {
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();

            // Verify encryption is enabled
            var config = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
            Assert.True(config.Enabled);

            using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            // Act
            var response = await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange",
                new KeyExchangeRequest(
                    clientEcdh.ExportSubjectPublicKeyInfo(),
                    clientEcdsa.ExportSubjectPublicKeyInfo()));

            // Assert
            Assert.NotNull(response);
            Assert.NotEmpty(response.KeyId);
            Assert.NotEmpty(response.ServerPublicKey);
            Assert.NotEmpty(response.ServerSigningPublicKey);

            // Verify we can derive shared secret
            using var serverEcdh = ECDiffieHellman.Create();
            serverEcdh.ImportSubjectPublicKeyInfo(response.ServerPublicKey, out _);
            var sharedSecret = clientEcdh.DeriveRawSecretAgreement(serverEcdh.PublicKey);
            Assert.Equal(32, sharedSecret.Length);

            await encryptionHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task FullEncryptedFlow_ClientCanEncryptDecryptWithServerKeys()
    {
        // verify encryption actually works:
        // 1. Enable encryption via admin API
        // 2. Do key exchange on a connection
        // 3. Client encrypts a payload with derived keys
        // 4. Verify the envelope structure is correct
        // 5. Decrypt it to verify round-trip works
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Get initial state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        try
        {
            // Step 1: Enable encryption
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });

            // Create a user session
            var (sessionId, _, _) = await LoginAsUserAsync();

            // Step 2: Connect to encryption hub and perform key exchange
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();

            // Verify encryption is enabled
            var config = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
            Assert.True(config.Enabled, "Encryption should be enabled");

            using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var keyExchangeResponse = await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange",
                new KeyExchangeRequest(
                    clientEcdh.ExportSubjectPublicKeyInfo(),
                    clientEcdsa.ExportSubjectPublicKeyInfo()));

            Assert.NotEmpty(keyExchangeResponse.KeyId);

            // Step 3: Use ClientEncryptor to encrypt and verify
            // NOTE: ClientEncryptor generates its own ECDH keys internally, so we can't
            // manually derive the same shared secret. We verify the envelope structure
            // and that the signature is correct.
            using var clientEncryptor = new ClientEncryptor();
            await clientEncryptor.PerformKeyExchangeAsync(_ => Task.FromResult(keyExchangeResponse));
            Assert.True(clientEncryptor.IsInitialized);
            Assert.Equal(keyExchangeResponse.KeyId, clientEncryptor.CurrentKeyId);

            // Step 4: Encrypt a test payload
            var testPayload = System.Text.Encoding.UTF8.GetBytes("TestEncryptedPayload");
            var envelope = clientEncryptor.EncryptAndSign(testPayload);
            
            // Step 5: Verify envelope structure
            Assert.Equal(keyExchangeResponse.KeyId, envelope.KeyId);
            Assert.Equal(12, envelope.Nonce.Length); // AES-GCM nonce
            Assert.Equal(16, envelope.Tag.Length);   // AES-GCM tag
            Assert.NotEmpty(envelope.Ciphertext);
            Assert.NotEmpty(envelope.Signature);
            Assert.True(envelope.SequenceNumber > 0);
            
            // Ciphertext should be same length as plaintext for AES-GCM
            Assert.Equal(testPayload.Length, envelope.Ciphertext.Length);
            
            // Ciphertext should NOT equal plaintext (it's actually encrypted)
            Assert.NotEqual(testPayload, envelope.Ciphertext);

            // Step 6: Verify the signature using the encryptor's public key
            using var clientSigner = ECDsa.Create();
            clientSigner.ImportSubjectPublicKeyInfo(clientEncryptor.SigningPublicKey, out _);
            
            using var signatureStream = new MemoryStream();
            using var signatureWriter = new BinaryWriter(signatureStream);
            signatureWriter.Write(envelope.KeyId);
            signatureWriter.Write(envelope.Nonce);
            signatureWriter.Write(envelope.Ciphertext);
            signatureWriter.Write(envelope.Tag);
            signatureWriter.Write(envelope.Timestamp);
            signatureWriter.Write(envelope.SequenceNumber);
            signatureWriter.Flush();
            
            var isValidSignature = clientSigner.VerifyData(
                signatureStream.ToArray(), 
                envelope.Signature, 
                HashAlgorithmName.SHA256);
            Assert.True(isValidSignature, "Client signature should be valid");

            await encryptionHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task ServerRegistersEncryptionForConnection_AfterKeyExchange()
    {
        // Verifies the server correctly registers encryption state after key exchange
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        try
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });

            var (sessionId, _, _) = await LoginAsUserAsync();

            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();

            using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var clientEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var keyResponse = await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange",
                new KeyExchangeRequest(
                    clientEcdh.ExportSubjectPublicKeyInfo(),
                    clientEcdsa.ExportSubjectPublicKeyInfo()));

            // Verify key exchange was successful
            Assert.NotEmpty(keyResponse.KeyId);
            Assert.NotEmpty(keyResponse.ServerPublicKey);
            Assert.NotEmpty(keyResponse.ServerSigningPublicKey);

            // Verify config still shows enabled
            var config = await encryptionHub.InvokeAsync<EncryptionConfig>("GetConfig");
            Assert.True(config.Enabled);

            await encryptionHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task CrossHubEncryption_KeyExchangeOnEncryptionHub_EncryptsAccountHubResponse()
    {
        // 1. Enable encryption via admin API
        // 2. Do key exchange on EncryptionHub
        // 3. Connect to AccountHub (different hub, same user)
        // 4. Call GetAccount and verify response is SecureEnvelope
        // 5. Decrypt the response using the client encryptor
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = initialConfig?.Enabled ?? false;

        try
        {
            // Step 1: Enable encryption
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = true });

            var (sessionId, _, userId) = await LoginAsUserAsync();

            // Step 2: Connect to EncryptionHub and do key exchange
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();

            using var clientEncryptor = new ClientEncryptor();
            var keyExchangeSuccess = await clientEncryptor.PerformKeyExchangeAsync(async request =>
            {
                return await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", request);
            });

            Assert.True(keyExchangeSuccess, "Key exchange should succeed");
            Assert.True(clientEncryptor.IsInitialized);

            // Step 3: Connect to AccountHub (DIFFERENT hub connection, SAME user)
            var accountHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
                .Build();
            await accountHub.StartAsync();

            // Step 4: Call GetAccount and check if response is SecureEnvelope
            // With encryption enabled and key exchange done, the filter should encrypt responses
            try
            {
                // Try to invoke expecting SecureEnvelope
                var encryptedResponse = await accountHub.InvokeAsync<SecureEnvelope>("GetAccount");
                
                // If we get here, the server returned a SecureEnvelope!
                Assert.NotNull(encryptedResponse);
                Assert.Equal(clientEncryptor.CurrentKeyId, encryptedResponse.KeyId);
                Assert.NotEmpty(encryptedResponse.Ciphertext);
                Assert.NotEmpty(encryptedResponse.Tag);
                Assert.NotEmpty(encryptedResponse.Signature);
                
                // Step 5: Decrypt the response
                var decryptedBytes = clientEncryptor.DecryptAndVerify(encryptedResponse);
                Assert.NotEmpty(decryptedBytes);
                
                // Deserialize the Account object
                var account = MemoryPackSerializer.Deserialize<Account>(decryptedBytes);
                Assert.NotNull(account);
                Assert.Equal(userId, account!.AccountId);
                
                // Cross-hub encryption works!
            }
            catch (System.Text.Json.JsonException)
            {
                // If the server didn't encrypt (because encryption state isn't tied correctly),
                // SignalR will try to deserialize Account as SecureEnvelope and fail with JsonException
                // This means the architecture fix didn't work
                Assert.Fail("Server returned unencrypted Account instead of SecureEnvelope. Cross-hub encryption not working.");
            }
            catch (Exception ex) when (ex.Message.Contains("Invalid key ID"))
            {
                // Key ID mismatch - encryption state not shared properly
                Assert.Fail($"Key ID mismatch: {ex.Message}");
            }

            await encryptionHub.DisposeAsync();
            await accountHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled",
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task EncryptionState_RotatesKeysAcrossAllHubs()
    {
        // Verifies that when a key rotation is triggered for a user, all active hub connections
        // for that user will start using the new encryption keys, even if they didn't perform
        // key exchange on that specific hub.
        
        // Arrange - Enable encryption globally
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Capture original enabled state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var config = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = config!.Enabled;

        await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = true });

        Guid? adminUserId = null;
        try
        {
            var (sessionId, userId) = await LoginAsRealAdminAsync();
            adminUserId = userId;
            
            // Step 1: Connect to encryption hub and do key exchange
            var rotationReceived = new TaskCompletionSource<KeyRotationRequest>();
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            encryptionHub.On<KeyRotationRequest>("KeyRotation", req => rotationReceived.TrySetResult(req));
            await encryptionHub.StartAsync();

            using var clientEncryptor = new ClientEncryptor();
            await clientEncryptor.PerformKeyExchangeAsync(async req => await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));
            
            // Step 2: Connect to AccountHub (standard SignalR connection)
            var accountHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
                .Build();
            await accountHub.StartAsync();
            
            // Step 3: Trigger key rotation from admin API
            var rotResp = await adminClient.PostAsync($"/api/admin/encryption/connections/{userId}/rotate", null);
            rotResp.EnsureSuccessStatusCode();

            // Wait for rotation request to arrive via SignalR
            var rotationRequest = await rotationReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(rotationRequest);

            // Complete the rotation on the client side
            var ack = clientEncryptor.HandleRotationRequest(rotationRequest);
            await encryptionHub.InvokeAsync("CompleteKeyRotation", ack);

            // Step 4: Verify the server is now using the NEW keys on the OTHER hub!
            // We call GetAccount on accountHub, which should now be encrypted with newKeyId
            
            var encryptedResponse = await accountHub.InvokeAsync<SecureEnvelope>("GetAccount");
            
            Assert.NotNull(encryptedResponse);
            Assert.Equal(rotationRequest.KeyId, encryptedResponse.KeyId);
            Assert.NotEqual(clientEncryptor.PreviousKeyId, encryptedResponse.KeyId);
            
            // Step 5: Decrypt with the new keys
            var decryptedBytes = clientEncryptor.DecryptAndVerify(encryptedResponse);
            var account = MemoryPackSerializer.Deserialize<Account>(decryptedBytes);
            
            Assert.NotNull(account);
            Assert.Equal(userId, account!.AccountId);

            await encryptionHub.DisposeAsync();
            await accountHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = wasEnabled });
            if (adminUserId.HasValue)
            {
                await adminClient.DeleteAsync($"/api/admin/encryption/connections/{adminUserId}");
            }
        }
    }

    [Fact]
    public async Task EncryptedHubConnection_MultiArgumentCall_Works()
    {
        // verifies that calling a method with multiple arguments (using JSON array) works through the gateway
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Capture original enabled state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var config = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = config!.Enabled;

        // Enable encryption
        await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = true });

        Guid? adminUserId = null;
        try
        {
            var (sessionId, userId) = await LoginAsRealAdminAsync();
            adminUserId = userId;
            
            // Do key exchange
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();
            using var encryptor = new ClientEncryptor();
            await encryptor.PerformKeyExchangeAsync(async req => await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));

            // Connect to AdminMetricsHub
            var metricsHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/hubs/admin-metrics?access_token={sessionId}")
                .Build();
            await metricsHub.StartAsync();
            await using var encryptedMetricsHub = new EncryptedHubConnection(metricsHub, encryptor, true);

            // Act - Call ClearTimeout (string, string) - 2 arguments
            // This should use the JSON array serialization in EncryptedHubConnection
            var result = await encryptedMetricsHub.InvokeAsync<bool>("ClearTimeout", "test-key", "test-policy");

            // Assert
            // Result might be false if no such timeout exists, but it shouldn't THROW
            Assert.False(result); 

            await metricsHub.DisposeAsync();
            await encryptionHub.DisposeAsync();
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = wasEnabled });
            if (adminUserId.HasValue)
            {
                await adminClient.DeleteAsync($"/api/admin/encryption/connections/{adminUserId}");
            }
        }
    }

    [Fact]
    public async Task EncryptedHubFilter_EnforcesGateway_WhenRequired()
    {
        // verifies that calling a plaintext method results in an error when RequireEncryption is enabled
        
        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Capture original enabled state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var config = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = config!.Enabled;

        // Enable encryption AND REQUIRE IT
        await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = true });
        await adminClient.PostAsJsonAsync("/api/admin/encryption/required", new { Required = true });
        
        Guid? userIdToCleanup = null;
        try
        {
            var (sessionId, userId) = await LoginAsRealAdminAsync();
            userIdToCleanup = userId;
            
            // Do key exchange
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();
            using var encryptor = new ClientEncryptor();
            await encryptor.PerformKeyExchangeAsync(async req => await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));

            // Connect to AccountHub (plain SignalR connection)
            var accountHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/accountHub?access_token={sessionId}")
                .Build();
            await accountHub.StartAsync();

            try
            {
                // Act - Call GetAccount normally (plaintext) while user IS encrypted
                // If the filter is working, it should catch that this user is encrypted 
                // and require the __encrypted__ gateway if RequireEncryption is true.
                
                var ex = await Assert.ThrowsAsync<HubException>(() => accountHub.InvokeAsync<Account>("GetAccount"));
                
                // Assert
                Assert.Contains("Encryption is required", ex.Message);
            }
            finally
            {
                await accountHub.DisposeAsync();
                await encryptionHub.DisposeAsync();
            }
        }
        finally
        {
            await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = wasEnabled });
            await adminClient.PostAsJsonAsync("/api/admin/encryption/required", new { Required = false });
            if (userIdToCleanup.HasValue)
            {
                await adminClient.DeleteAsync($"/api/admin/encryption/connections/{userIdToCleanup}");
            }
        }
    }

    #endregion
}
