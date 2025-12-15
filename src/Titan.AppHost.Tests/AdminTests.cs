using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for admin-only operations.
/// </summary>
[Collection("AppHost")]
public class AdminTests : IntegrationTestBase
{
    public AdminTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanCreateBaseType()
    {
        // Arrange
        await using var admin = await CreateAdminSessionAsync();
        var hub = await admin.GetBaseTypeHubAsync();

        var baseType = new BaseType
        {
            BaseTypeId = $"admin-test-item-{Guid.NewGuid():N}",
            Name = "Admin Test Item",
            Description = "Created by admin test",
            Category = ItemCategory.Currency,
            Slot = EquipmentSlot.None,
            Width = 1,
            Height = 1,
            MaxStackSize = 10,
            IsTradeable = true
        };

        // Act
        var created = await hub.InvokeAsync<BaseType>("Create", baseType);

        // Assert
        Assert.NotNull(created);
        Assert.Equal(baseType.BaseTypeId, created.BaseTypeId);
        Assert.Equal(baseType.Name, created.Name);
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
    public async Task User_CannotCreateBaseType_ThrowsException()
    {
        // Arrange - Regular user, not admin
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetBaseTypeHubAsync();

        var baseType = new BaseType
        {
            BaseTypeId = "user-should-not-create",
            Name = "Unauthorized Item",
            Category = ItemCategory.Currency,
            MaxStackSize = 1,
            IsTradeable = false
        };

        // Act & Assert - Should throw due to missing Admin role
        await Assert.ThrowsAsync<HubException>(() => 
            hub.InvokeAsync<BaseType>("Create", baseType));
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
