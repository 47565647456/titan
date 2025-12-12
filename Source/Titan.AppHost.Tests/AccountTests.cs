using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for account and character management.
/// </summary>
[Collection("AppHost")]
public class AccountTests : IntegrationTestBase
{
    public AccountTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task NewUser_HasEmptyAccount()
    {
        // Arrange
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetAccountHubAsync();

        // Act
        var account = await hub.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Empty(account.UnlockedCosmetics);
        Assert.Empty(account.UnlockedAchievements);
    }

    [Fact]
    public async Task CreateCharacter_AppearsInGetCharacters()
    {
        // Arrange
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetAccountHubAsync();

        // Act
        var created = await hub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", 
            "standard",  // seasonId
            "TestHero",  // name
            CharacterRestrictions.None);
        
        var characters = await hub.InvokeAsync<IReadOnlyList<CharacterSummary>>("GetCharacters");

        // Assert
        Assert.NotNull(created);
        Assert.Equal("TestHero", created.Name);
        Assert.Single(characters);
        Assert.Equal(created.CharacterId, characters[0].CharacterId);
    }

    [Fact]
    public async Task User_CannotAccessOtherUserAccount()
    {
        // Arrange - Two different users
        await using var user1 = await CreateUserSessionAsync();
        await using var user2 = await CreateUserSessionAsync();
        
        // User1 creates a character
        var hub1 = await user1.GetAccountHubAsync();
        await hub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "User1Hero", CharacterRestrictions.None);

        // User2 tries to access - should only see their own (empty) data
        var hub2 = await user2.GetAccountHubAsync();
        var characters = await hub2.InvokeAsync<IReadOnlyList<CharacterSummary>>("GetCharacters");
        
        // Assert - User2 should have no characters (IDOR prevented)
        Assert.Empty(characters);
    }

    [Fact]
    public async Task FullOnboardingFlow_LoginCreateCharacterGetInventory()
    {
        // Step 1: Create session (login happens automatically)
        await using var user = await CreateUserSessionAsync();
        Assert.NotNull(user.Token);

        // Step 2: Create character
        var accountHub = await user.GetAccountHubAsync();
        var character = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "OnboardingHero", CharacterRestrictions.None);
        Assert.NotNull(character);

        // Step 3: Access inventory (reuses connection)
        var inventoryHub = await user.GetInventoryHubAsync();
        var inventory = await inventoryHub.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", character.CharacterId, "standard");
        Assert.NotNull(inventory);
        Assert.Empty(inventory);  // New character has empty inventory

        // Step 4: Add an item
        var newItem = await inventoryHub.InvokeAsync<Item>(
            "AddItem", character.CharacterId, "standard", "starter_sword", 1, (Dictionary<string, object>?)null);
        Assert.NotNull(newItem);
        Assert.Equal("starter_sword", newItem.ItemTypeId);

        // Step 5: Verify item in inventory
        inventory = await inventoryHub.InvokeAsync<IReadOnlyList<Item>>(
            "GetInventory", character.CharacterId, "standard");
        Assert.Single(inventory);
        Assert.Equal(newItem.Id, inventory[0].Id);
    }
}
