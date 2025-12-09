using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing a character's inventory within a season.
/// Key: (CharacterId, SeasonId)
/// </summary>
public interface IInventoryGrain : IGrainWithGuidCompoundKey
{
    Task<List<Item>> GetItemsAsync();
    Task<Item?> GetItemAsync(Guid itemId);
    Task<Item> AddItemAsync(string itemTypeId, int quantity = 1, Dictionary<string, object>? metadata = null);
    Task ReceiveItemAsync(Item item);
    Task<bool> RemoveItemAsync(Guid itemId);
    Task<bool> HasItemAsync(Guid itemId);
}
