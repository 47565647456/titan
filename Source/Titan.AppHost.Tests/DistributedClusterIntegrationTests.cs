using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for distributed cluster behavior.
/// </summary>
[Collection("AppHost")]
public class DistributedClusterIntegrationTests : IntegrationTestBase
{
    public DistributedClusterIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task MultipleUsers_CanCreateCharactersConcurrently()
    {
        // Arrange - Create multiple users in parallel
        var userTasks = Enumerable.Range(0, 5)
            .Select(_ => CreateUserSessionAsync())
            .ToArray();
        
        var users = await Task.WhenAll(userTasks);

        // Act - Each user creates a character via AccountHub
        var characterTasks = users.Select(async (user, i) =>
        {
            var accountHub = await user.GetAccountHubAsync();
            return await accountHub.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Concurrent{i}_{Guid.NewGuid():N}", CharacterRestrictions.None);
        }).ToArray();

        var characters = await Task.WhenAll(characterTasks);

        // Assert
        Assert.All(characters, c => Assert.NotNull(c));
        Assert.Equal(5, characters.Length);

        // Cleanup
        foreach (var user in users)
        {
            await user.DisposeAsync();
        }
    }

    [Fact]
    public async Task SequentialOperations_GetStats_WorksCorrectly()
    {
        // Arrange
        var user = await CreateUserSessionAsync();
        
        // Act - Create character via AccountHub
        var accountHub = await user.GetAccountHubAsync();
        var character = await accountHub.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", $"Sequential_{Guid.NewGuid():N}", CharacterRestrictions.None);
        
        Assert.NotNull(character);
        
        // Get inventory hub and get stats (requires characterId and seasonId)
        var inventoryHub = await user.GetInventoryHubAsync();
        var stats = await inventoryHub.InvokeAsync<CharacterStats>("GetStats", character!.CharacterId, "standard");
        
        // Assert
        Assert.NotNull(stats);

        // Cleanup
        await user.DisposeAsync();
    }
}
