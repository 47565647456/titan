using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Titan.AppHost.Tests;
using Titan.Client.Encryption;
using Titan.Abstractions.Models;
using Titan.Client;
using System.Net.Http.Json;
using Xunit.Abstractions;
using MemoryPack;

namespace Titan.AppHost.Tests;

[Collection("AppHost")]
public class EncryptedBroadcastTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public EncryptedBroadcastTests(AppHostFixture fixture, ITestOutputHelper output) : base(fixture) 
    {
        _output = output;
    }

    [Fact]
    public async Task EncryptedBroadcast_ReceivesSecureEnvelope()
    {
        // 1. Enable encryption
        // 2. Client A performs key exchange and subscribes to broadcasts
        // 3. Admin triggers a broadcast (e.g. creates a season)
        // 4. Verification: Client A should receive a SecureEnvelope, NOT the raw object

        // Arrange
        var adminClient = await CreateAuthenticatedAdminClientAsync();
        
        // Capture original enabled state
        var configResponse = await adminClient.GetAsync("/api/admin/encryption/config");
        var config = await configResponse.Content.ReadFromJsonAsync<EncryptionConfigResponse>();
        var wasEnabled = config!.Enabled;

        await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = true });

        Guid? userIdToCleanup = null;

        try 
        {
            var (sessionId, userId) = await LoginAsRealAdminAsync();
            userIdToCleanup = userId;

            // Step 1: Crypto Setup
            var encryptionHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/encryptionHub?access_token={sessionId}")
                .Build();
            await encryptionHub.StartAsync();

            using var encryptor = new ClientEncryptor();
            await encryptor.PerformKeyExchangeAsync(async req => await encryptionHub.InvokeAsync<KeyExchangeResponse>("KeyExchange", req));
            
            // Step 2: Connect to SeasonHub and Subscribe
            var seasonHub = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/seasonHub?access_token={sessionId}")
                .Build();

            var tcs = new TaskCompletionSource<SecureEnvelope>();

            // Listen for "SeasonEvent". Since we are encrypted, the server sends SecureEnvelope.
            seasonHub.On<SecureEnvelope>("SeasonEvent", (envelope) => 
            {
                tcs.TrySetResult(envelope);
            });

            await seasonHub.StartAsync();
            
            // Join group - RPC call might also return encrypted result if verified, but this is void/Task
            await seasonHub.InvokeAsync("JoinAllSeasonsGroup");

            // Step 3: Trigger Broadcast via Admin API
            var seasonId = $"test-season-{Guid.NewGuid()}";
            var createSeasonCmd = new 
            {
                SeasonId = seasonId,
                Name = "Encrypted Broadcast Test",
                Type = "Standard",
                StartDate = DateTimeOffset.UtcNow,
                Status = "Upcoming"
            };

            // Call CreateSeason. Since encryption is enabled, this returns SecureEnvelope, NOT Season.
            // We invoke expecting SecureEnvelope to avoid deserialization error.
            var rpcResponseEnvelope = await seasonHub.InvokeAsync<SecureEnvelope>("CreateSeason", 
                createSeasonCmd.SeasonId, 
                createSeasonCmd.Name, 
                SeasonType.Permanent, 
                createSeasonCmd.StartDate, 
                null, 
                SeasonStatus.Upcoming, 
                null, null, false);
            
            // Quick verify of RPC response
            Assert.NotNull(rpcResponseEnvelope);
            Assert.Equal(encryptor.CurrentKeyId, rpcResponseEnvelope.KeyId);
            
            // Step 4: Verify Broadcast
            var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            
            Assert.NotNull(envelope);
            Assert.Equal(encryptor.CurrentKeyId, envelope.KeyId);
            Assert.NotEmpty(envelope.Ciphertext);

            // Verify we can decrypt it
            var decryptedBytes = encryptor.DecryptAndVerify(envelope);
            // The decrypted payload is a JSON string of the object
            // Just verifying it decrypts successfully is sufficient proof of encryption flow
            // But we can check content too
            try 
            {
                 // SeasonEvent is JSON serialized into the envelope payload
                 // It's not MemoryPack serialized in the broadcast service (it uses System.Text.Json)
                 var json = System.Text.Encoding.UTF8.GetString(decryptedBytes);
                 Assert.Contains(seasonId, json);
                 _output.WriteLine("Successfully received and decrypted broadcast!");
            }
            catch (Exception ex)
            {
                throw new Exception($"Payload decryption succeeded but content check failed: {ex.Message}");
            }

            await seasonHub.DisposeAsync();
            await encryptionHub.DisposeAsync();
        }
        finally
        {
             await adminClient.PostAsJsonAsync("/api/admin/encryption/enabled", new { Enabled = wasEnabled });
             if (userIdToCleanup.HasValue)
             {
                 await adminClient.DeleteAsync($"/api/admin/encryption/connections/{userIdToCleanup}");
             }
        }
    }

    private record EncryptionConfigResponse(bool Enabled, bool Required);
}
