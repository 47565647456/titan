using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for inventory operations.
/// All operations verify the character belongs to the authenticated user.
/// </summary>
[Authorize]
public class InventoryHub : TitanHubBase
{
    public InventoryHub(IClusterClient clusterClient, ILogger<InventoryHub> logger)
        : base(clusterClient, logger)
    {
    }

    /// <summary>
    /// Get bag grid state for a character.
    /// </summary>
    public async Task<InventoryGrid> GetBagGrid(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.GetBagGridAsync();
    }

    /// <summary>
    /// Get all items in a character's bag.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Item>> GetBagItems(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.GetBagItemsAsync();
    }

    /// <summary>
    /// Get equipped items for a character.
    /// </summary>
    public async Task<IReadOnlyDictionary<EquipmentSlot, Item>> GetEquipped(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.GetEquippedAsync();
    }

    /// <summary>
    /// Move an item within the bag.
    /// </summary>
    public async Task<bool> MoveBagItem(Guid characterId, string seasonId, Guid itemId, int newX, int newY)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.MoveBagItemAsync(itemId, newX, newY);
    }

    /// <summary>
    /// Equip an item from the bag.
    /// </summary>
    public async Task<EquipResult> Equip(Guid characterId, string seasonId, Guid bagItemId, EquipmentSlot slot)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.EquipAsync(bagItemId, slot);
    }

    /// <summary>
    /// Unequip an item to the bag.
    /// </summary>
    public async Task<Item?> Unequip(Guid characterId, string seasonId, EquipmentSlot slot)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.UnequipAsync(slot);
    }

    /// <summary>
    /// Get character stats.
    /// </summary>
    public async Task<CharacterStats> GetStats(Guid characterId, string seasonId)
    {
        await VerifyCharacterOwnershipAsync(characterId);
        HubValidation.ValidateId(seasonId, nameof(seasonId));

        var grain = ClusterClient.GetGrain<ICharacterInventoryGrain>(characterId, seasonId);
        return await grain.GetStatsAsync();
    }
}

