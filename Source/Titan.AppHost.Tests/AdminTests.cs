using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for admin-only operations.
/// </summary>
[Collection("AppHost")]
public class AdminTests : IntegrationTestBase
{
    public AdminTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanCreateItemType()
    {
        // Arrange
        await using var admin = await CreateAdminSessionAsync();
        var hub = await admin.GetItemTypeHubAsync();

        var definition = new ItemTypeDefinition
        {
            ItemTypeId = $"admin-test-item-{Guid.NewGuid():N}",
            Name = "Admin Test Item",
            Description = "Created by admin test",
            MaxStackSize = 10,
            IsTradeable = true,
            Category = "test"
        };

        // Act
        var created = await hub.InvokeAsync<ItemTypeDefinition>("Create", definition);

        // Assert
        Assert.NotNull(created);
        Assert.Equal(definition.ItemTypeId, created.ItemTypeId);
        Assert.Equal(definition.Name, created.Name);
    }

    [Fact]
    public async Task Admin_CanCreateSeason()
    {
        // Arrange
        await using var admin = await CreateAdminSessionAsync();
        var hub = await admin.GetSeasonHubAsync();
        var seasonId = $"admin-test-season-{Guid.NewGuid():N}";

        // Act
        var created = await hub.InvokeAsync<Season>(
            "CreateSeason",
            seasonId,
            "Admin Test Season",
            SeasonType.Temporary,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7),
            SeasonStatus.Active,
            "standard",
            (Dictionary<string, object>?)null,
            false); // isVoid

        // Assert
        Assert.NotNull(created);
        Assert.Equal(seasonId, created.SeasonId);
        Assert.Equal("Admin Test Season", created.Name);
    }

    [Fact]
    public async Task User_CannotCreateItemType_ThrowsException()
    {
        // Arrange - Regular user, not admin
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetItemTypeHubAsync();

        var definition = new ItemTypeDefinition
        {
            ItemTypeId = "user-should-not-create",
            Name = "Unauthorized Item",
            MaxStackSize = 1,
            IsTradeable = false
        };

        // Act & Assert - Should throw due to missing Admin role
        await Assert.ThrowsAsync<HubException>(() => 
            hub.InvokeAsync<ItemTypeDefinition>("Create", definition));
    }

    [Fact]
    public async Task User_CannotCreateSeason_ThrowsException()
    {
        // Arrange - Regular user, not admin
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetSeasonHubAsync();

        // Act & Assert - Should throw due to missing Admin role
        await Assert.ThrowsAsync<HubException>(() => 
            hub.InvokeAsync<Season>(
                "CreateSeason",
                "user-should-not-create",
                "Unauthorized Season",
                SeasonType.Temporary,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(1),
                SeasonStatus.Upcoming,
                "standard",
                (Dictionary<string, object>?)null,
                false)); // isVoid
    }
}
