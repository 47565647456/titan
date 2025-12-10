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
        var (token, _) = await LoginAsUserAsync();
        var hub = await ConnectToHubAsync("/accountHub", token);

        // Act
        var account = await hub.InvokeAsync<Account>("GetAccount");

        // Assert
        Assert.NotNull(account);
        Assert.Empty(account.UnlockedCosmetics);
        Assert.Empty(account.UnlockedAchievements);
        
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task CreateCharacter_AppearsInGetCharacters()
    {
        // Arrange
        var (token, _) = await LoginAsUserAsync();
        var hub = await ConnectToHubAsync("/accountHub", token);

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
        
        await hub.DisposeAsync();
    }

    [Fact]
    public async Task User_CannotAccessOtherUserAccount()
    {
        // Arrange - Two different users
        var (token1, userId1) = await LoginAsUserAsync();
        var (token2, userId2) = await LoginAsUserAsync();
        
        // User1 creates a character
        var hub1 = await ConnectToHubAsync("/accountHub", token1);
        await hub1.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "User1Hero", CharacterRestrictions.None);
        await hub1.DisposeAsync();

        // User2 tries to access - should only see their own (empty) data
        var hub2 = await ConnectToHubAsync("/accountHub", token2);
        var characters = await hub2.InvokeAsync<IReadOnlyList<CharacterSummary>>("GetCharacters");
        
        // Assert - User2 should have no characters (IDOR prevented)
        Assert.Empty(characters);
        
        await hub2.DisposeAsync();
    }

    [Fact]
    public async Task FullOnboardingFlow_LoginCreateCharacterGetInventory()
    {
        // Step 1: Login
        var (token, userId) = await LoginAsUserAsync();
        Assert.NotNull(token);

        // Step 2: Create character
        var accountHub = await ConnectToHubAsync("/accountHub", token);
        var character = await accountHub.InvokeAsync<CharacterSummary>(
            "CreateCharacter", "standard", "OnboardingHero", CharacterRestrictions.None);
        Assert.NotNull(character);
        await accountHub.DisposeAsync();

        // Step 3: Access inventory
        var inventoryHub = await ConnectToHubAsync("/inventoryHub", token);
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
        
        await inventoryHub.DisposeAsync();
    }
}
