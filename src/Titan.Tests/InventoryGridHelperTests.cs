using Titan.Abstractions.Helpers;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for InventoryGridHelper static methods.
/// </summary>
public class InventoryGridHelperTests
{
    #region CanPlace Tests

    [Fact]
    public void CanPlace_EmptyGrid_ReturnsTrue()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var result = InventoryGridHelper.CanPlace(grid, 0, 0, 2, 2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanPlace_OccupiedCells_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act
        var result = InventoryGridHelper.CanPlace(grid, 0, 0, 2, 2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanPlace_PartialOverlap_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act - try to place overlapping item
        var result = InventoryGridHelper.CanPlace(grid, 1, 1, 2, 2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanPlace_OutOfBounds_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act - try to place item that extends beyond grid
        var result = InventoryGridHelper.CanPlace(grid, 9, 9, 2, 2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanPlace_NegativeCoordinates_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var result = InventoryGridHelper.CanPlace(grid, -1, 0, 1, 1);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanPlace_ExactFit_ReturnsTrue()
    {
        // Arrange
        var grid = InventoryGrid.Create(2, 2);

        // Act - item exactly fills the grid
        var result = InventoryGridHelper.CanPlace(grid, 0, 0, 2, 2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanPlace_AdjacentToExisting_ReturnsTrue()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act - place adjacent item (no overlap)
        var result = InventoryGridHelper.CanPlace(grid, 2, 0, 2, 2);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region FindSpace Tests

    [Fact]
    public void FindSpace_EmptyGrid_ReturnsOrigin()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var result = InventoryGridHelper.FindSpace(grid, 2, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.Value.X);
        Assert.Equal(0, result.Value.Y);
    }

    [Fact]
    public void FindSpace_PartiallyFilled_FindsGap()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act
        var result = InventoryGridHelper.FindSpace(grid, 2, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Value.X);
        Assert.Equal(0, result.Value.Y);
    }

    [Fact]
    public void FindSpace_FullGrid_ReturnsNull()
    {
        // Arrange
        var grid = InventoryGrid.Create(2, 2);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act
        var result = InventoryGridHelper.FindSpace(grid, 1, 1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindSpace_LargeItem_FindsSpace()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        // Place small items leaving a 4x4 gap at (5,5)
        InventoryGridHelper.Place(grid, Guid.NewGuid(), 0, 0, 5, 10);

        // Act
        var result = InventoryGridHelper.FindSpace(grid, 4, 4);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Value.X);
        Assert.Equal(0, result.Value.Y);
    }

    [Fact]
    public void FindSpace_ItemTooLarge_ReturnsNull()
    {
        // Arrange
        var grid = InventoryGrid.Create(5, 5);

        // Act - item larger than grid
        var result = InventoryGridHelper.FindSpace(grid, 6, 6);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Place and Remove Tests

    [Fact]
    public void PlaceItem_UpdatesCellsAndPlacements()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();

        // Act
        InventoryGridHelper.Place(grid, itemId, 3, 4, 2, 3);

        // Assert - cells are occupied
        Assert.Equal(itemId, grid.Cells[3][4]);
        Assert.Equal(itemId, grid.Cells[4][4]);
        Assert.Equal(itemId, grid.Cells[3][5]);
        Assert.Equal(itemId, grid.Cells[4][5]);
        Assert.Equal(itemId, grid.Cells[3][6]);
        Assert.Equal(itemId, grid.Cells[4][6]);

        // Assert - placement is recorded
        Assert.True(grid.Placements.ContainsKey(itemId));
        Assert.Equal(3, grid.Placements[itemId].X);
        Assert.Equal(4, grid.Placements[itemId].Y);
    }

    [Fact]
    public void RemoveItem_ClearsCellsAndPlacements()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act
        var result = InventoryGridHelper.Remove(grid, itemId, 2, 2);

        // Assert
        Assert.True(result);
        Assert.Null(grid.Cells[0][0]);
        Assert.Null(grid.Cells[1][0]);
        Assert.Null(grid.Cells[0][1]);
        Assert.Null(grid.Cells[1][1]);
        Assert.False(grid.Placements.ContainsKey(itemId));
    }

    [Fact]
    public void RemoveItem_NonExistentItem_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var result = InventoryGridHelper.Remove(grid, Guid.NewGuid(), 2, 2);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Move Tests

    [Fact]
    public void MoveItem_UpdatesPlacementAndCells()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 0, 0, 2, 2);

        // Act
        var result = InventoryGridHelper.Move(grid, itemId, 5, 5, 2, 2);

        // Assert
        Assert.True(result);

        // Old cells should be empty
        Assert.Null(grid.Cells[0][0]);
        Assert.Null(grid.Cells[1][1]);

        // New cells should be occupied
        Assert.Equal(itemId, grid.Cells[5][5]);
        Assert.Equal(itemId, grid.Cells[6][6]);

        // Placement should be updated
        Assert.Equal(5, grid.Placements[itemId].X);
        Assert.Equal(5, grid.Placements[itemId].Y);
    }

    [Fact]
    public void MoveItem_ToOccupiedPosition_ReturnsFalse()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var item1 = Guid.NewGuid();
        var item2 = Guid.NewGuid();
        InventoryGridHelper.Place(grid, item1, 0, 0, 2, 2);
        InventoryGridHelper.Place(grid, item2, 5, 5, 2, 2);

        // Act
        var result = InventoryGridHelper.Move(grid, item1, 5, 5, 2, 2);

        // Assert
        Assert.False(result);
        // Original position should be unchanged
        Assert.Equal(0, grid.Placements[item1].X);
        Assert.Equal(0, grid.Placements[item1].Y);
    }

    [Fact]
    public void MoveItem_ToSamePosition_ReturnsTrue()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 3, 3, 2, 2);

        // Act - move to same position (should work because we ignore own cells)
        var result = InventoryGridHelper.Move(grid, itemId, 3, 3, 2, 2);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetPlacement and GetFreeCellCount Tests

    [Fact]
    public void GetPlacement_ExistingItem_ReturnsPlacement()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        var itemId = Guid.NewGuid();
        InventoryGridHelper.Place(grid, itemId, 3, 4, 1, 1);

        // Act
        var placement = InventoryGridHelper.GetPlacement(grid, itemId);

        // Assert
        Assert.NotNull(placement);
        Assert.Equal(3, placement!.X);
        Assert.Equal(4, placement.Y);
    }

    [Fact]
    public void GetPlacement_NonExistent_ReturnsNull()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var placement = InventoryGridHelper.GetPlacement(grid, Guid.NewGuid());

        // Assert
        Assert.Null(placement);
    }

    [Fact]
    public void GetFreeCellCount_EmptyGrid_ReturnsAllCells()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);

        // Act
        var count = InventoryGridHelper.GetFreeCellCount(grid);

        // Assert
        Assert.Equal(100, count);
    }

    [Fact]
    public void GetFreeCellCount_PartiallyFilled_ReturnsCorrectCount()
    {
        // Arrange
        var grid = InventoryGrid.Create(10, 10);
        InventoryGridHelper.Place(grid, Guid.NewGuid(), 0, 0, 2, 2); // 4 cells
        InventoryGridHelper.Place(grid, Guid.NewGuid(), 5, 5, 3, 3); // 9 cells

        // Act
        var count = InventoryGridHelper.GetFreeCellCount(grid);

        // Assert
        Assert.Equal(87, count); // 100 - 4 - 9
    }

    #endregion
}
