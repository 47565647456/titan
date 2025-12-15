using Orleans.TestingHost;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for AccountStashGrain.
/// </summary>
[Collection(ClusterCollection.Name)]
public class AccountStashGrainTests
{
    private readonly TestCluster _cluster;

    public AccountStashGrainTests(ClusterFixture fixture)
    {
        _cluster = fixture.Cluster;
    }

    private async Task<string> SeedBaseType()
    {
        var baseTypeId = $"stash_{Guid.NewGuid():N}";
        var registry = _cluster.GrainFactory.GetGrain<IBaseTypeRegistryGrain>("default");
        var baseType = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = $"Test {baseTypeId}",
            Slot = EquipmentSlot.MainHand,
            Width = 1,
            Height = 2,
            Category = ItemCategory.Equipment,
            Tags = new HashSet<string> { "test", "weapon" }
        };
        await registry.RegisterAsync(baseType);
        return baseTypeId;
    }

    #region Tab Management Tests

    [Fact]
    public async Task GetTabsAsync_EmptyStash_ReturnsEmpty()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);

        // Act
        var tabs = await grain.GetTabsAsync();

        // Assert
        Assert.NotNull(tabs);
        Assert.Empty(tabs);
    }

    [Fact]
    public async Task CreateTabAsync_CreatesNewTab()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);

        // Act
        var tab = await grain.CreateTabAsync("Main Stash");

        // Assert
        Assert.NotNull(tab);
        Assert.Equal("Main Stash", tab.Name);
        Assert.NotEqual(Guid.Empty, tab.TabId);
    }

    [Fact]
    public async Task CreateTabAsync_WithType_SetsCorrectType()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);

        // Act
        var tab = await grain.CreateTabAsync("Currency", StashTabType.Currency);

        // Assert
        Assert.Equal(StashTabType.Currency, tab.Type);
    }

    [Fact]
    public async Task GetTabAsync_ExistingTab_ReturnsTab()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var created = await grain.CreateTabAsync("My Tab");

        // Act
        var retrieved = await grain.GetTabAsync(created.TabId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(created.TabId, retrieved.TabId);
        Assert.Equal("My Tab", retrieved.Name);
    }

    [Fact]
    public async Task GetTabAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);

        // Act
        var result = await grain.GetTabAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RenameTabAsync_ExistingTab_RenamesSuccessfully()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Old Name");

        // Act
        var renamed = await grain.RenameTabAsync(tab.TabId, "New Name");

        // Assert
        Assert.NotNull(renamed);
        Assert.Equal("New Name", renamed.Name);
    }

    [Fact]
    public async Task DeleteTabAsync_ExistingTab_DeletesSuccessfully()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("To Delete");

        // Act
        var deleted = await grain.DeleteTabAsync(tab.TabId);

        // Assert
        Assert.True(deleted);
        
        var retrieved = await grain.GetTabAsync(tab.TabId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task SetTabAffinityAsync_SetsAffinity()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Equipment Tab");

        // Act
        await grain.SetTabAffinityAsync(tab.TabId, ItemCategory.Equipment);

        // Assert
        var updated = await grain.GetTabAsync(tab.TabId);
        Assert.NotNull(updated);
        Assert.Equal(ItemCategory.Equipment, updated.Affinity);
    }

    #endregion

    #region Grid Operations Tests

    [Fact]
    public async Task GetTabGridAsync_NewTab_ReturnsEmptyGrid()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Grid Tab");

        // Act
        var grid = await grain.GetTabGridAsync(tab.TabId);

        // Assert
        Assert.NotNull(grid);
    }

    [Fact]
    public async Task DepositAsync_ValidPosition_DepositsItem()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Deposit Test");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await grain.DepositAsync(tab.TabId, item, 0, 0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DepositAutoAsync_FindsSpace()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Auto Deposit");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var position = await grain.DepositAutoAsync(tab.TabId, item);

        // Assert
        Assert.NotNull(position);
    }

    [Fact]
    public async Task WithdrawAsync_ExistingItem_ReturnsItem()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Withdraw Test");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.DepositAsync(tab.TabId, item, 0, 0);

        // Act
        var withdrawn = await grain.WithdrawAsync(tab.TabId, item.Id);

        // Assert
        Assert.NotNull(withdrawn);
        Assert.Equal(item.Id, withdrawn.Id);
    }

    [Fact]
    public async Task MoveItemAsync_ValidMove_Succeeds()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Move Test", gridWidth: 12, gridHeight: 12);
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.DepositAsync(tab.TabId, item, 0, 0);

        // Act
        var moved = await grain.MoveItemAsync(tab.TabId, item.Id, 5, 5);

        // Assert
        Assert.True(moved);
    }

    [Fact]
    public async Task GetTabItemsAsync_WithItems_ReturnsItems()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var grain = _cluster.GrainFactory.GetGrain<IAccountStashGrain>(accountId);
        var tab = await grain.CreateTabAsync("Items Test");
        var baseTypeId = await SeedBaseType();
        
        var item = new Item
        {
            Id = Guid.NewGuid(),
            BaseTypeId = baseTypeId,
            ItemLevel = 10,
            Rarity = ItemRarity.Normal,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await grain.DepositAsync(tab.TabId, item, 0, 0);

        // Act
        var items = await grain.GetTabItemsAsync(tab.TabId);

        // Assert
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Contains(items, kvp => kvp.Key == item.Id);
    }

    #endregion
}
