using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// Character stats used for equipment requirement validation.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("CharacterStats")]
public partial record CharacterStats
{
    /// <summary>
    /// Character level.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public int Level { get; init; } = 1;

    /// <summary>
    /// Strength attribute.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public int Strength { get; init; }

    /// <summary>
    /// Dexterity attribute.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public int Dexterity { get; init; }

    /// <summary>
    /// Intelligence attribute.
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public int Intelligence { get; init; }
}
