using Microsoft.Extensions.Options;
using Titan.API.Config;
using Titan.API.Services.Auth;

namespace Titan.Tests;

/// <summary>
/// Tests for TokenService JWT generation.
/// </summary>
public class TokenServiceTests
{
    private static ITokenService CreateTokenService(
        string? key = null, 
        string? issuer = null, 
        int? accessTokenExpirationMinutes = null,
        int? refreshTokenExpirationMinutes = null)
    {
        var options = new JwtOptions
        {
            Key = key ?? "TestSecretKeyThatIsAtLeast32BytesLong!!",
            Issuer = issuer ?? "TestIssuer",
            Audience = "TestAudience",
            AccessTokenExpirationMinutes = accessTokenExpirationMinutes ?? 15,
            RefreshTokenExpirationMinutes = refreshTokenExpirationMinutes ?? 10080
        };

        return new TokenService(Options.Create(options));
    }

    [Fact]
    public void GenerateToken_ShouldReturnValidJwtString()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();

        // Act
        var token = service.GenerateAccessToken(userId, "Mock");

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.Contains(".", token); // JWT format: header.payload.signature
        Assert.Equal(2, token.Count(c => c == '.')); // Should have exactly 2 dots
    }

    [Fact]
    public void GenerateToken_ShouldIncludeUserIdClaim()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();

        // Act
        var token = service.GenerateAccessToken(userId, "Mock");
        var claims = ParseJwtClaims(token);

        // Assert
        Assert.True(claims.ContainsKey("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"));
        Assert.Equal(userId.ToString(), claims["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"]);
    }

    [Fact]
    public void GenerateToken_ShouldIncludeProviderClaim()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();

        // Act
        var token = service.GenerateAccessToken(userId, "EOS");
        var claims = ParseJwtClaims(token);

        // Assert
        Assert.True(claims.ContainsKey("provider"));
        Assert.Equal("EOS", claims["provider"]);
    }

    [Fact]
    public void GenerateToken_WithRoles_ShouldIncludeRoleClaims()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();
        var roles = new[] { "User", "Admin" };

        // Act
        var token = service.GenerateAccessToken(userId, "Mock", roles);
        var payload = GetJwtPayload(token);

        // Assert - role claim can be array or single value
        Assert.Contains("role", payload);
    }

    [Fact]
    public void GenerateToken_ShouldIncludeJtiClaim()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();

        // Act
        var token = service.GenerateAccessToken(userId, "Mock");
        var claims = ParseJwtClaims(token);

        // Assert
        Assert.True(claims.ContainsKey("jti"));
        Assert.True(Guid.TryParse(claims["jti"], out _));
    }

    [Fact]
    public void GenerateToken_ShouldGenerateUniqueTokens()
    {
        // Arrange
        var service = CreateTokenService();
        var userId = Guid.NewGuid();

        // Act
        var token1 = service.GenerateAccessToken(userId, "Mock");
        var token2 = service.GenerateAccessToken(userId, "Mock");

        // Assert - tokens should differ due to unique JTI and timestamp
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void AccessTokenExpiration_ShouldMatchConfiguration()
    {
        // Arrange
        var service = CreateTokenService(accessTokenExpirationMinutes: 30);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(30), service.AccessTokenExpiration);
    }

    [Fact]
    public void RefreshTokenExpiration_ShouldMatchConfiguration()
    {
        // Arrange
        var service = CreateTokenService(refreshTokenExpirationMinutes: 1440); // 1 day

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(1440), service.RefreshTokenExpiration);
    }

    #region Helpers

    private static Dictionary<string, string> ParseJwtClaims(string token)
    {
        var payload = GetJwtPayload(token);
        var claims = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payload)!;
        
        return claims.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? "");
    }

    private static string GetJwtPayload(string token)
    {
        var parts = token.Split('.');
        var payload = parts[1];
        
        // Add padding if needed for base64
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }
        
        var bytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    #endregion
}
