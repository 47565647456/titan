using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
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
        Assert.NotNull(client.SessionId);
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
    public async Task TitanClient_GetBagItems_ViaInventoryClient()
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

        // Act - Use new grid-based inventory methods
        var inventoryClient = await client.GetInventoryClientAsync();
        var bagItems = await inventoryClient.GetBagItems(character.CharacterId, "standard");

        // Assert
        Assert.NotNull(bagItems);
        Assert.Empty(bagItems); // New character has empty inventory
    }

    [Fact]
    public async Task TitanClient_GetBagGrid_ViaInventoryClient()
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

        // Act - Use new grid-based inventory methods
        var inventoryClient = await client.GetInventoryClientAsync();
        var bagGrid = await inventoryClient.GetBagGrid(character.CharacterId, "standard");

        // Assert
        Assert.NotNull(bagGrid);
        Assert.Equal(12, bagGrid.Width); // Default bag size
        Assert.Equal(5, bagGrid.Height);
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
    public async Task TitanClient_Logout_ClearsSession()
    {
        // Arrange
        await using var client = new TitanClientBuilder()
            .WithBaseUrl(ApiBaseUrl)
            .Build();

        await client.Auth.LoginAsync($"mock:{Guid.NewGuid()}", "Mock");
        Assert.True(client.IsAuthenticated);

        // Act
        await client.Auth.LogoutAsync();

        // Assert
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.SessionId);
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

        // Step 4: Access inventory (new grid-based system)
        var inventoryClient = await client.GetInventoryClientAsync();
        var bagGrid = await inventoryClient.GetBagGrid(character.CharacterId, "standard");
        Assert.NotNull(bagGrid);
        Assert.Equal(12, bagGrid.Width);

        // Step 5: Get bag items (empty for new character)
        var bagItems = await inventoryClient.GetBagItems(character.CharacterId, "standard");
        Assert.Empty(bagItems);

        // Step 6: Get character stats
        var stats = await inventoryClient.GetStats(character.CharacterId, "standard");
        Assert.NotNull(stats);
        Assert.Equal(1, stats.Level); // Default level
    }
}
