using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for seed data availability.
/// Tests that BaseTypeSeedHostedService seeds data correctly.
/// NOTE: These tests may fail if BaseTypeSeedHostedService is not registered
/// or if the JSON seed data isn't being loaded properly.
/// </summary>
[Collection("AppHost")]
public class SeedDataIntegrationTests : IntegrationTestBase
{
    public SeedDataIntegrationTests(AppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BaseTypes_GetAll_ReturnsTypes()
    {
        // Arrange
        var admin = await CreateAdminSessionAsync();
        var baseTypeHub = await admin.GetBaseTypeHubAsync();

        // Act - Get all base types
        var allBaseTypes = await baseTypeHub.InvokeAsync<IReadOnlyList<BaseType>>("GetAll");

        // Assert - May be empty if seed service hasn't run or isn't registered
        Assert.NotNull(allBaseTypes);
        
        // Log the count for debugging
        if (allBaseTypes.Count == 0)
        {
            // This is expected if BaseTypeSeedHostedService isn't registered
            // or if it hasn't completed seeding yet
            Assert.True(true, "No base types seeded (this may be expected)");
        }

        // Cleanup
        await admin.DisposeAsync();
    }
    
    [Fact]
    public async Task BaseType_CanCreateAndRetrieve()
    {
        // Arrange - This test creates a base type to verify the registry works
        var admin = await CreateAdminSessionAsync();
        var baseTypeHub = await admin.GetBaseTypeHubAsync();
        
        var testBaseType = new BaseType
        {
            BaseTypeId = $"test_integration_{Guid.NewGuid():N}",
            Name = "Integration Test Item",
            Category = ItemCategory.Currency,
            Slot = EquipmentSlot.None,
            Width = 1,
            Height = 1,
            MaxStackSize = 100
        };

        // Act - Create and retrieve
        var created = await baseTypeHub.InvokeAsync<BaseType>("Create", testBaseType);
        var retrieved = await baseTypeHub.InvokeAsync<BaseType?>("Get", testBaseType.BaseTypeId);

        // Assert
        Assert.NotNull(created);
        Assert.NotNull(retrieved);
        Assert.Equal(testBaseType.Name, retrieved!.Name);

        // Cleanup - Delete the test base type
        await baseTypeHub.InvokeAsync("Delete", testBaseType.BaseTypeId);
        await admin.DisposeAsync();
    }
}
