using Titan.Abstractions.Models;
using Titan.Client;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the TitanClient SDK.
/// Tests the actual client SDK against the running API.
/// </summary>
[Collection("AppHost")]
public class TitanClientIntegrationTests : IntegrationTestBase
{
    public TitanClientIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task TitanClient_Login_SetsAuthState()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        // Act
        var result = await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");

        // Assert
        Assert.True(result.Success);
        Assert.True(client.IsAuthenticated);
        Assert.NotNull(client.AccessToken);
        Assert.NotNull(client.UserId);
        Assert.Equal(result.UserId, client.UserId);
    }

    [Fact]
    public async Task TitanClient_GetAccountClient_AfterLogin_Works()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");

        // Act
        var accountClient = await client.GetAccountClientAsync();
        var account = await accountClient.GetAccount();

        // Assert
        Assert.NotNull(account);
        Assert.Equal(client.UserId, account.AccountId);
    }

    [Fact]
    public async Task TitanClient_CreateCharacter_ViaAccountClient()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");

        var accountClient = await client.GetAccountClientAsync();

        // Act
        var character = await accountClient.CreateCharacter(
            "standard",
            $"TestChar_{Guid.NewGuid():N}",
            CharacterRestrictions.None);

        // Assert
        Assert.NotNull(character);
        Assert.Equal("standard", character.SeasonId);
    }

    [Fact]
    public async Task TitanClient_GetInventory_ViaInventoryClient()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");

        var accountClient = await client.GetAccountClientAsync();
        var character = await accountClient.CreateCharacter(
            "standard",
            $"TestChar_{Guid.NewGuid():N}",
            CharacterRestrictions.None);

        // Act
        var inventoryClient = await client.GetInventoryClientAsync();
        var inventory = await inventoryClient.GetInventory(character.CharacterId, "standard");

        // Assert
        Assert.NotNull(inventory);
        Assert.Empty(inventory);
    }

    [Fact]
    public async Task TitanClient_AddItem_ViaInventoryClient()
    {
        // Arrange - Login as admin for item creation
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:admin:{Guid.NewGuid()}", "Mock");

        var accountClient = await client.GetAccountClientAsync();
        var character = await accountClient.CreateCharacter(
            "standard",
            $"TestChar_{Guid.NewGuid():N}",
            CharacterRestrictions.None);

        var inventoryClient = await client.GetInventoryClientAsync();

        // Act
        var item = await inventoryClient.AddItem(
            character.CharacterId,
            "standard",
            "test_sword",
            1,
            null);

        // Assert
        Assert.NotNull(item);
        Assert.Equal("test_sword", item.ItemTypeId);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public async Task TitanClient_GetProviders_ReturnsAvailableProviders()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        // Act - GetProviders doesn't require authentication
        var providers = await client.Auth.GetProvidersAsync();

        // Assert
        Assert.NotNull(providers);
        Assert.NotEmpty(providers);
        Assert.Contains("Mock", providers);
    }

    [Fact]
    public async Task TitanClient_Refresh_UpdatesToken()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        var loginResult = await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");
        var originalToken = client.AccessToken;

        // Act
        var refreshResult = await client.Auth.RefreshAsync(
            loginResult.RefreshToken!,
            loginResult.UserId!.Value);

        // Assert
        Assert.NotEqual(originalToken, client.AccessToken);
        Assert.Equal(refreshResult.AccessToken, client.AccessToken);
    }

    [Fact]
    public async Task TitanClient_MultipleHubClients_ReuseConnections()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");

        // Act - Get account client twice
        var client1 = await client.GetAccountClientAsync();
        var client2 = await client.GetAccountClientAsync();

        // Both should work and be the same underlying connection
        var account1 = await client1.GetAccount();
        var account2 = await client2.GetAccount();

        // Assert
        Assert.Equal(account1.AccountId, account2.AccountId);
    }

    [Fact]
    public async Task TitanClient_FullOnboardingFlow_WorksEndToEnd()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .WithAutoReconnect(true)
            .Build();

        // Step 1: Login
        var loginResult = await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");
        Assert.True(loginResult.Success);

        // Step 2: Get account
        var accountClient = await client.GetAccountClientAsync();
        var account = await accountClient.GetAccount();
        Assert.NotNull(account);

        // Step 3: Create character
        var character = await accountClient.CreateCharacter(
            "standard",
            "OnboardingHero",
            CharacterRestrictions.None);
        Assert.NotNull(character);

        // Step 4: Access inventory
        var inventoryClient = await client.GetInventoryClientAsync();
        var inventory = await inventoryClient.GetInventory(character.CharacterId, "standard");
        Assert.Empty(inventory);

        // Step 5: Add item (as admin would normally register item types first)
        var item = await inventoryClient.AddItem(
            character.CharacterId,
            "standard",
            "starter_sword",
            1,
            null);
        Assert.NotNull(item);

        // Step 6: Verify inventory has item
        var finalInventory = await inventoryClient.GetInventory(character.CharacterId, "standard");
        Assert.Single(finalInventory);
    }
}
