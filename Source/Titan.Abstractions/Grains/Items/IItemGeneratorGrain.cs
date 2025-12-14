using Orleans;
using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Grains.Items;

/// <summary>
/// Stateless worker for generating items.
/// Handles item creation and crafting operations.
/// Implementation should be marked with [StatelessWorker].
/// Key: "default"
/// </summary>
public interface IItemGeneratorGrain : IGrainWithStringKey
{
    /// <summary>
    /// Generates a normal (white) item from a base type.
    /// </summary>
    Task<Item> GenerateAsync(string baseTypeId, int itemLevel);

    /// <summary>
    /// Generates an item with a specific rarity.
    /// </summary>
    Task<Item> GenerateAsync(string baseTypeId, int itemLevel, ItemRarity rarity);

    /// <summary>
    /// Generates a unique item.
    /// </summary>
    Task<Item> GenerateUniqueAsync(string uniqueId);

    /// <summary>
    /// Rolls sockets for an item based on its base type.
    /// </summary>
    Task<List<Socket>> RollSocketsAsync(string baseTypeId, int itemLevel);

    /// <summary>
    /// Transmutes a normal item to magic (adds 1-2 mods).
    /// Returns null if item is not normal rarity.
    /// </summary>
    Task<Item?> TransmuteAsync(Item item);

    /// <summary>
    /// Rerolls modifiers on a magic item.
    /// Returns null if item is not magic rarity.
    /// </summary>
    Task<Item?> AlterAsync(Item item);

    /// <summary>
    /// Upgrades a magic item to rare (adds more mods).
    /// Returns null if item is not magic rarity.
    /// </summary>
    Task<Item?> RegalAsync(Item item);

    /// <summary>
    /// Rerolls all modifiers on a rare item.
    /// Returns null if item is not rare rarity.
    /// </summary>
    Task<Item?> ChaosAsync(Item item);

    /// <summary>
    /// Adds a random modifier to a rare item.
    /// Returns null if item is not rare or already has max mods.
    /// </summary>
    Task<Item?> ExaltAsync(Item item);

    /// <summary>
    /// Rerolls the number of sockets on an item.
    /// </summary>
    Task<Item> JewellerAsync(Item item);

    /// <summary>
    /// Rerolls the links between sockets on an item.
    /// </summary>
    Task<Item> FusingAsync(Item item);

    /// <summary>
    /// Rerolls socket colors on an item.
    /// </summary>
    Task<Item> ChromaticAsync(Item item);
}
