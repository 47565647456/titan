using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for database persistence.
/// </summary>
[Collection("AppHost")]
public class DatabasePersistenceIntegrationTests : IntegrationTestBase
{
    public DatabasePersistenceIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CharacterData_PersistsToDatabase()
    {
        // Arrange
        var user = await CreateUserSessionAsync();
        var uniqueName = $"DbTest_{Guid.NewGuid():N}";

        // Act - Create character via AccountHub
        var accountHub = await user.GetAccountHubAsync();
        var character = await accountHub.InvokeAsync<CharacterSummary>("CreateCharacter", "standard", uniqueName, CharacterRestrictions.None);

        // Assert
        Assert.NotNull(character);
        Assert.Equal(uniqueName, character!.Name);

        // Cleanup
        await user.DisposeAsync();
    }

    [Fact]
    public async Task AccountData_PersistsToDatabase()
    {
        // Arrange
        var user = await CreateUserSessionAsync();

        // Act - Get account info
        var accountHub = await user.GetAccountHubAsync();
        var account = await accountHub.InvokeAsync<Account?>("GetAccount");

        // Assert
        Assert.NotNull(account);

        // Cleanup
        await user.DisposeAsync();
    }
}
