using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing a player's inventory.
/// Key: UserId (Guid)
/// </summary>
public interface IInventoryGrain : IGrainWithGuidKey
{
    Task<List<Item>> GetItemsAsync();
    Task<Item?> GetItemAsync(Guid itemId);
    Task<Item> AddItemAsync(string itemTypeId, int quantity = 1, Dictionary<string, object>? metadata = null);
    Task<bool> RemoveItemAsync(Guid itemId);
    Task<bool> HasItemAsync(Guid itemId);
}
