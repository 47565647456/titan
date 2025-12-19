using Titan.API.Validators;
using Xunit;

namespace Titan.Tests;

/// <summary>
/// Unit tests for hub validation DTOs and FluentValidation validators.
/// </summary>
public class HubValidatorTests
{
    #region IdRequestValidator Tests

    [Fact]
    public void IdValidator_ValidId_Succeeds()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var request = new IdRequest("valid-id", "testParam");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void IdValidator_NullId_Fails()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var request = new IdRequest(null, "testParam");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("testParam is required", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void IdValidator_EmptyId_Fails()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var request = new IdRequest("", "seasonId");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("seasonId is required", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void IdValidator_WhitespaceId_Fails()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var request = new IdRequest("   ", "baseTypeId");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("baseTypeId is required", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void IdValidator_ExactMaxLength_Succeeds()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var id = new string('x', 100);
        var request = new IdRequest(id, "testParam");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void IdValidator_ExceedsMaxLength_Fails()
    {
        // Arrange
        var validator = new IdRequestValidator();
        var id = new string('x', 101);
        var request = new IdRequest(id, "seasonId");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("seasonId exceeds maximum length of 100", result.Errors[0].ErrorMessage);
    }

    #endregion

    #region NameRequestValidator Tests

    [Fact]
    public void NameValidator_ValidName_Succeeds()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var request = new NameRequest("Valid Name", "testParam");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NameValidator_NullName_Fails()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var request = new NameRequest(null, "name");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("name is required", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void NameValidator_EmptyName_Fails()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var request = new NameRequest("", "characterName");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("characterName is required", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void NameValidator_ExactDefaultMaxLength_Succeeds()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var name = new string('x', 200);
        var request = new NameRequest(name, "testParam");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NameValidator_ExceedsDefaultMaxLength_Fails()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var name = new string('x', 201);
        var request = new NameRequest(name, "name");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("name exceeds maximum length of 200", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void NameValidator_CustomMaxLength_Succeeds()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var name = new string('x', 50);
        var request = new NameRequest(name, "testParam", 50);

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NameValidator_ExceedsCustomMaxLength_Fails()
    {
        // Arrange
        var validator = new NameRequestValidator();
        var name = new string('x', 51);
        var request = new NameRequest(name, "characterName", 50);

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("characterName exceeds maximum length of 50", result.Errors[0].ErrorMessage);
    }

    #endregion

    #region NonNegativeValueRequestValidator Tests

    [Fact]
    public void NonNegativeValidator_PositiveValue_Succeeds()
    {
        // Arrange
        var validator = new NonNegativeValueRequestValidator();
        var request = new NonNegativeValueRequest(100, "amount");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonNegativeValidator_Zero_Succeeds()
    {
        // Arrange
        var validator = new NonNegativeValueRequestValidator();
        var request = new NonNegativeValueRequest(0, "amount");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void NonNegativeValidator_NegativeValue_Fails()
    {
        // Arrange
        var validator = new NonNegativeValueRequestValidator();
        var request = new NonNegativeValueRequest(-1, "amount");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("amount cannot be negative", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void NonNegativeValidator_LargeNegativeValue_Fails()
    {
        // Arrange
        var validator = new NonNegativeValueRequestValidator();
        var request = new NonNegativeValueRequest(-1000000, "progress");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("progress cannot be negative", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void NonNegativeValidator_MaxLongValue_Succeeds()
    {
        // Arrange
        var validator = new NonNegativeValueRequestValidator();
        var request = new NonNegativeValueRequest(long.MaxValue, "experience");

        // Act
        var result = validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion
}
