using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models.Items;

/// <summary>
/// A 2D grid for inventory storage with item placement tracking.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
[Alias("InventoryGrid")]
public partial record InventoryGrid
{
    /// <summary>
    /// Grid width in cells.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public int Width { get; init; } = 12;

    /// <summary>
    /// Grid height in cells.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public int Height { get; init; } = 5;

    /// <summary>
    /// 2D array tracking which item occupies each cell.
    /// null = empty, Guid = item ID occupying that cell.
    /// Items spanning multiple cells have their ID in all occupied cells.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public Guid?[][] Cells { get; init; } = Array.Empty<Guid?[]>();

    /// <summary>
    /// Map of item ID to its placement position (top-left corner).
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public Dictionary<Guid, GridPlacement> Placements { get; init; } = new();

    /// <summary>
    /// Creates a new empty grid with the specified dimensions.
    /// </summary>
    public static InventoryGrid Create(int width, int height)
    {
        var cells = new Guid?[width][];
        for (int x = 0; x < width; x++)
        {
            cells[x] = new Guid?[height];
        }

        return new InventoryGrid
        {
            Width = width,
            Height = height,
            Cells = cells,
            Placements = new Dictionary<Guid, GridPlacement>()
        };
    }
}

/// <summary>
/// Tracks the position of an item in a grid.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record GridPlacement
{
    /// <summary>
    /// The item's unique ID.
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required Guid ItemId { get; init; }

    /// <summary>
    /// X position (column, 0-based).
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public int X { get; init; }

    /// <summary>
    /// Y position (row, 0-based).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public int Y { get; init; }
}
