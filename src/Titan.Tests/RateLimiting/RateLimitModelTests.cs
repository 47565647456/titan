using Titan.Abstractions.Models;

namespace Titan.Tests.RateLimiting;

/// <summary>
/// Tests for rate limiting model parsing and formatting.
/// </summary>
public class RateLimitModelTests
{
    [Fact]
    public void RateLimitRule_Parse_ValidFormat_ReturnsRule()
    {
        // Arrange
        var ruleString = "100:60:300";

        // Act
        var rule = RateLimitRule.Parse(ruleString);

        // Assert
        Assert.Equal(100, rule.MaxHits);
        Assert.Equal(60, rule.PeriodSeconds);
        Assert.Equal(300, rule.TimeoutSeconds);
    }

    [Fact]
    public void RateLimitRule_ToString_ReturnsProperFormat()
    {
        // Arrange
        var rule = new RateLimitRule(10, 60, 600);

        // Act
        var result = rule.ToString();

        // Assert
        Assert.Equal("10:60:600", result);
    }

    [Fact]
    public void RateLimitRule_Parse_InvalidFormat_Throws()
    {
        // Arrange
        var invalidRule = "100:60"; // Missing timeout

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RateLimitRule.Parse(invalidRule));
    }

    [Fact]
    public void RateLimitPolicy_ToHeaderValue_ReturnsCommaSeparatedRules()
    {
        // Arrange
        var policy = new RateLimitPolicy("Auth", 
        [
            new RateLimitRule(10, 60, 600),
            new RateLimitRule(30, 300, 1800)
        ]);

        // Act
        var headerValue = policy.ToHeaderValue();

        // Assert
        Assert.Equal("10:60:600,30:300:1800", headerValue);
    }

    [Fact]
    public void RateLimitRuleState_ToString_WithoutTimeout_ReturnsThreePartFormat()
    {
        // Arrange
        var state = new RateLimitRuleState(5, 60, 55, null);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Equal("5:60:55", result);
    }

    [Fact]
    public void RateLimitRuleState_ToString_WithTimeout_ReturnsFourPartFormat()
    {
        // Arrange
        var state = new RateLimitRuleState(10, 60, 0, 300);

        // Act
        var result = state.ToString();

        // Assert
        Assert.Equal("10:60:0:300", result);
    }

    [Fact]
    public void RateLimitingConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new RateLimitingConfiguration();

        // Assert
        Assert.True(config.Enabled);
        Assert.Empty(config.Policies);
        Assert.Empty(config.EndpointMappings);
        Assert.Equal("Global", config.DefaultPolicyName);
    }

    [Fact]
    public void RateLimitPolicy_SingleRule_ReturnsCorrectHeaderValue()
    {
        // Arrange
        var policy = new RateLimitPolicy("Global", 
        [
            new RateLimitRule(100, 60, 300)
        ]);

        // Act
        var headerValue = policy.ToHeaderValue();

        // Assert
        Assert.Equal("100:60:300", headerValue);
    }

    [Fact]
    public void RateLimitRule_Parse_LargeNumbers_ParsesCorrectly()
    {
        // Arrange - high volume policy like "1000 requests per hour, 1 hour timeout"
        var ruleString = "1000:3600:3600";

        // Act
        var rule = RateLimitRule.Parse(ruleString);

        // Assert
        Assert.Equal(1000, rule.MaxHits);
        Assert.Equal(3600, rule.PeriodSeconds);
        Assert.Equal(3600, rule.TimeoutSeconds);
    }

    [Theory]
    [InlineData("abc:60:300", "MaxHits")]
    [InlineData("100:xyz:300", "PeriodSeconds")]
    [InlineData("100:60:foo", "TimeoutSeconds")]
    public void RateLimitRule_Parse_NonNumericValues_ThrowsWithDescriptiveMessage(string input, string expectedField)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => RateLimitRule.Parse(input));
        Assert.Contains(expectedField, ex.Message);
        Assert.Contains("positive integer", ex.Message);
    }

    [Theory]
    [InlineData("0:60:300", "MaxHits")]
    [InlineData("100:0:300", "PeriodSeconds")]
    [InlineData("100:60:0", "TimeoutSeconds")]
    public void RateLimitRule_Parse_ZeroValues_ThrowsWithDescriptiveMessage(string input, string expectedField)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => RateLimitRule.Parse(input));
        Assert.Contains(expectedField, ex.Message);
        Assert.Contains("positive integer", ex.Message);
    }

    [Theory]
    [InlineData("-5:60:300", "MaxHits")]
    [InlineData("100:-1:300", "PeriodSeconds")]
    [InlineData("100:60:-10", "TimeoutSeconds")]
    public void RateLimitRule_Parse_NegativeValues_ThrowsWithDescriptiveMessage(string input, string expectedField)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => RateLimitRule.Parse(input));
        Assert.Contains(expectedField, ex.Message);
        Assert.Contains("positive integer", ex.Message);
    }
}
