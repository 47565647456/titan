using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Result of checking equipment requirements.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("RequirementCheckResult")]
public partial record RequirementCheckResult
{
    /// <summary>
    /// Whether the character can equip the item.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public bool CanEquip { get; init; }

    /// <summary>
    /// List of requirement failures (empty if CanEquip is true).
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public List<string> FailedRequirements { get; init; } = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static RequirementCheckResult Success => new() { CanEquip = true };

    /// <summary>
    /// Creates a failed result with the specified reasons.
    /// </summary>
    public static RequirementCheckResult Failure(params string[] reasons) =>
        new() { CanEquip = false, FailedRequirements = reasons.ToList() };
}

/// <summary>
/// Result of an equip operation.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("EquipResult")]
public partial record EquipResult
{
    /// <summary>
    /// Whether the equip operation succeeded.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public bool Success { get; init; }

    /// <summary>
    /// The item that was equipped (if successful).
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public Item? EquippedItem { get; init; }

    /// <summary>
    /// The item that was unequipped (if there was one in the slot).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public Item? UnequippedItem { get; init; }

    /// <summary>
    /// Error messages (if failed).
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public List<string> Errors { get; init; } = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static EquipResult Succeeded(Item equipped, Item? unequipped = null) =>
        new() { Success = true, EquippedItem = equipped, UnequippedItem = unequipped };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static EquipResult Failed(params string[] errors) =>
        new() { Success = false, Errors = errors.ToList() };
}
