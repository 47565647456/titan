using Titan.Abstractions.Models.Items;

namespace Titan.Abstractions.Helpers;

/// <summary>
/// Helper methods for inventory grid operations.
/// </summary>
public static class InventoryGridHelper
{
    /// <summary>
    /// Checks if an item can be placed at the specified position.
    /// </summary>
    /// <param name="grid">The inventory grid.</param>
    /// <param name="x">X position (column).</param>
    /// <param name="y">Y position (row).</param>
    /// <param name="width">Item width in cells.</param>
    /// <param name="height">Item height in cells.</param>
    /// <returns>True if placement is valid, false otherwise.</returns>
    public static bool CanPlace(InventoryGrid grid, int x, int y, int width, int height)
    {
        // Bounds check
        if (x < 0 || y < 0 || x + width > grid.Width || y + height > grid.Height)
            return false;

        // Check all cells the item would occupy
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                if (grid.Cells[x + dx][y + dy] != null)
                    return false; // Cell is occupied
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if an item can be moved to a new position (excludes its current cells).
    /// </summary>
    public static bool CanMove(InventoryGrid grid, Guid itemId, int newX, int newY, int width, int height)
    {
        // Bounds check
        if (newX < 0 || newY < 0 || newX + width > grid.Width || newY + height > grid.Height)
            return false;

        // Check all cells, but ignore cells occupied by this item
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                var cellOccupant = grid.Cells[newX + dx][newY + dy];
                if (cellOccupant != null && cellOccupant != itemId)
                    return false; // Cell is occupied by another item
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the first available position for an item.
    /// </summary>
    /// <param name="grid">The inventory grid.</param>
    /// <param name="width">Item width in cells.</param>
    /// <param name="height">Item height in cells.</param>
    /// <returns>Position tuple, or null if no space available.</returns>
    public static (int X, int Y)? FindSpace(InventoryGrid grid, int width, int height)
    {
        // Scan row by row, then column by column
        for (int y = 0; y <= grid.Height - height; y++)
        {
            for (int x = 0; x <= grid.Width - width; x++)
            {
                if (CanPlace(grid, x, y, width, height))
                    return (x, y);
            }
        }

        return null;
    }

    /// <summary>
    /// Places an item in the grid at the specified position.
    /// Does not validate - call CanPlace first.
    /// </summary>
    public static void Place(InventoryGrid grid, Guid itemId, int x, int y, int width, int height)
    {
        // Mark all cells as occupied
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                grid.Cells[x + dx][y + dy] = itemId;
            }
        }

        // Record placement
        grid.Placements[itemId] = new GridPlacement { ItemId = itemId, X = x, Y = y };
    }

    /// <summary>
    /// Removes an item from the grid.
    /// </summary>
    public static bool Remove(InventoryGrid grid, Guid itemId, int width, int height)
    {
        if (!grid.Placements.TryGetValue(itemId, out var placement))
            return false;

        // Clear all cells occupied by this item
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                grid.Cells[placement.X + dx][placement.Y + dy] = null;
            }
        }

        // Remove placement record
        grid.Placements.Remove(itemId);
        return true;
    }

    /// <summary>
    /// Moves an item to a new position in the grid.
    /// </summary>
    public static bool Move(InventoryGrid grid, Guid itemId, int newX, int newY, int width, int height)
    {
        if (!grid.Placements.TryGetValue(itemId, out var currentPlacement))
            return false;

        // Check if we can move to the new position
        if (!CanMove(grid, itemId, newX, newY, width, height))
            return false;

        // Clear old position
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                grid.Cells[currentPlacement.X + dx][currentPlacement.Y + dy] = null;
            }
        }

        // Set new position
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                grid.Cells[newX + dx][newY + dy] = itemId;
            }
        }

        // Update placement record
        grid.Placements[itemId] = new GridPlacement { ItemId = itemId, X = newX, Y = newY };
        return true;
    }

    /// <summary>
    /// Gets the placement for an item.
    /// </summary>
    public static GridPlacement? GetPlacement(InventoryGrid grid, Guid itemId)
    {
        return grid.Placements.TryGetValue(itemId, out var placement) ? placement : null;
    }

    /// <summary>
    /// Calculates the total number of free cells in the grid.
    /// </summary>
    public static int GetFreeCellCount(InventoryGrid grid)
    {
        int count = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid.Cells[x][y] == null)
                    count++;
            }
        }
        return count;
    }
}
