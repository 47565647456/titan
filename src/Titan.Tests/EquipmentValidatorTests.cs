using Titan.Abstractions.Helpers;
using Titan.Abstractions.Models.Items;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for EquipmentValidator static methods.
/// </summary>
public class EquipmentValidatorTests
{
    #region CanEquip Tests

    [Fact]
    public void CanEquip_MeetsAllRequirements_ReturnsSuccess()
    {
        // Arrange
        var stats = new CharacterStats { Level = 20, Strength = 50, Dexterity = 30, Intelligence = 20 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_sword",
            Name = "Test Sword",
            Slot = EquipmentSlot.MainHand,
            RequiredLevel = 10,
            RequiredStrength = 40
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.True(result.CanEquip);
        Assert.Empty(result.FailedRequirements);
    }

    [Fact]
    public void CanEquip_LevelTooLow_ReturnsFalse()
    {
        // Arrange
        var stats = new CharacterStats { Level = 5 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_sword",
            Name = "Test Sword",
            Slot = EquipmentSlot.MainHand,
            RequiredLevel = 10
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Contains(result.FailedRequirements, r => r.Contains("Level"));
    }

    [Fact]
    public void CanEquip_StrengthTooLow_ReturnsFalse()
    {
        // Arrange
        var stats = new CharacterStats { Level = 20, Strength = 10 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_sword",
            Name = "Test Sword",
            Slot = EquipmentSlot.MainHand,
            RequiredStrength = 50
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Contains(result.FailedRequirements, r => r.Contains("Str"));
    }

    [Fact]
    public void CanEquip_DexterityTooLow_ReturnsFalse()
    {
        // Arrange
        var stats = new CharacterStats { Level = 20, Dexterity = 10 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_bow",
            Name = "Test Bow",
            Slot = EquipmentSlot.MainHand,
            RequiredDexterity = 50
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Contains(result.FailedRequirements, r => r.Contains("Dex"));
    }

    [Fact]
    public void CanEquip_IntelligenceTooLow_ReturnsFalse()
    {
        // Arrange
        var stats = new CharacterStats { Level = 20, Intelligence = 10 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_wand",
            Name = "Test Wand",
            Slot = EquipmentSlot.MainHand,
            RequiredIntelligence = 50
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Contains(result.FailedRequirements, r => r.Contains("Int"));
    }

    [Fact]
    public void CanEquip_MultipleFailures_ListsAll()
    {
        // Arrange
        var stats = new CharacterStats { Level = 5, Strength = 10, Dexterity = 10 };
        var baseType = new BaseType
        {
            BaseTypeId = "test_item",
            Name = "Test Item",
            Slot = EquipmentSlot.MainHand,
            RequiredLevel = 10,
            RequiredStrength = 50,
            RequiredDexterity = 50
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Equal(3, result.FailedRequirements.Count);
    }

    [Fact]
    public void CanEquip_NonEquippableItem_ReturnsFalse()
    {
        // Arrange
        var stats = new CharacterStats { Level = 100 };
        var baseType = new BaseType
        {
            BaseTypeId = "currency",
            Name = "Currency",
            Slot = EquipmentSlot.None // Currency can't be equipped
        };

        // Act
        var result = EquipmentValidator.CanEquip(stats, baseType);

        // Assert
        Assert.False(result.CanEquip);
        Assert.Contains(result.FailedRequirements, r => r.Contains("cannot be equipped"));
    }

    #endregion

    #region IsSlotValid Tests

    [Fact]
    public void IsSlotValid_ExactMatch_ReturnsTrue()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "test_helmet", Name = "Test Helmet", Slot = EquipmentSlot.Helmet };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.Helmet);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSlotValid_RingLeftToRight_ReturnsTrue()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "test_ring", Name = "Test Ring", Slot = EquipmentSlot.RingLeft };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.RingRight);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSlotValid_RingRightToLeft_ReturnsTrue()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "test_ring", Name = "Test Ring", Slot = EquipmentSlot.RingRight };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.RingLeft);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSlotValid_MainHandToOffHand_ReturnsTrue()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "test_sword", Name = "Test Sword", Slot = EquipmentSlot.MainHand };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.OffHand);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSlotValid_HelmetToBodyArmour_ReturnsFalse()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "test_helmet", Name = "Test Helmet", Slot = EquipmentSlot.Helmet };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.BodyArmour);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsSlotValid_NoneSlot_ReturnsFalse()
    {
        // Arrange
        var baseType = new BaseType { BaseTypeId = "currency", Name = "Currency", Slot = EquipmentSlot.None };

        // Act
        var result = EquipmentValidator.IsSlotValid(baseType, EquipmentSlot.MainHand);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region CalculateAttributeBonuses Tests

    [Fact]
    public void CalculateAttributeBonuses_ZeroStats_ReturnsZero()
    {
        // Arrange
        var stats = new CharacterStats();

        // Act
        var (bonusLife, bonusMana, bonusAccuracy) = EquipmentValidator.CalculateAttributeBonuses(stats);

        // Assert
        Assert.Equal(0, bonusLife);
        Assert.Equal(0, bonusMana);
        Assert.Equal(0, bonusAccuracy);
    }

    [Fact]
    public void CalculateAttributeBonuses_CorrectFormulas()
    {
        // Arrange
        var stats = new CharacterStats
        {
            Strength = 100,     // Should give +50 life (100/10 * 5)
            Dexterity = 50,     // Should give +100 accuracy (50/10 * 20)
            Intelligence = 80   // Should give +40 mana (80/10 * 5)
        };

        // Act
        var (bonusLife, bonusMana, bonusAccuracy) = EquipmentValidator.CalculateAttributeBonuses(stats);

        // Assert
        Assert.Equal(50, bonusLife);
        Assert.Equal(40, bonusMana);
        Assert.Equal(100, bonusAccuracy);
    }

    [Fact]
    public void CalculateAttributeBonuses_PartialValues_TruncatesCorrectly()
    {
        // Arrange - values that don't divide evenly by 10
        var stats = new CharacterStats
        {
            Strength = 15,      // Should give +5 life (15/10 = 1, 1*5 = 5)
            Dexterity = 25,     // Should give +40 accuracy (25/10 = 2, 2*20 = 40)
            Intelligence = 9    // Should give +0 mana (9/10 = 0)
        };

        // Act
        var (bonusLife, bonusMana, bonusAccuracy) = EquipmentValidator.CalculateAttributeBonuses(stats);

        // Assert
        Assert.Equal(5, bonusLife);
        Assert.Equal(0, bonusMana);
        Assert.Equal(40, bonusAccuracy);
    }

    #endregion

    #region GetDisabledSlots Tests

    [Fact]
    public void GetDisabledSlots_AllRequirementsMet_ReturnsEmpty()
    {
        // Arrange
        var stats = new CharacterStats { Level = 50, Strength = 100, Dexterity = 50, Intelligence = 50 };
        var equipped = new Dictionary<EquipmentSlot, (Item, BaseType)>
        {
            [EquipmentSlot.MainHand] = (TestItemFactory.CreateTestSword(), new BaseType
            {
                BaseTypeId = "test_sword",
                Name = "Test Sword",
                Slot = EquipmentSlot.MainHand,
                RequiredLevel = 10,
                RequiredStrength = 50
            })
        };

        // Act
        var result = EquipmentValidator.GetDisabledSlots(stats, equipped);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDisabledSlots_RequirementsNotMet_ReturnsSlots()
    {
        // Arrange
        var stats = new CharacterStats { Level = 10, Strength = 20 }; // Not enough strength
        var equipped = new Dictionary<EquipmentSlot, (Item, BaseType)>
        {
            [EquipmentSlot.MainHand] = (TestItemFactory.CreateTestSword(), new BaseType
            {
                BaseTypeId = "test_sword",
                Name = "Test Sword",
                Slot = EquipmentSlot.MainHand,
                RequiredLevel = 10,
                RequiredStrength = 50 // Requires 50 but have 20
            })
        };

        // Act
        var result = EquipmentValidator.GetDisabledSlots(stats, equipped);

        // Assert
        Assert.Single(result);
        Assert.Equal(EquipmentSlot.MainHand, result[0]);
    }

    #endregion
}
