using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Titan.API.Config;

namespace Titan.API.Services.Auth;

/// <summary>
/// JWT access token generation service.
/// Generates short-lived signed tokens for authenticated users.
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtOptions _options;

    public TimeSpan AccessTokenExpiration => TimeSpan.FromMinutes(_options.AccessTokenExpirationMinutes);
    public TimeSpan RefreshTokenExpiration => TimeSpan.FromMinutes(_options.RefreshTokenExpirationMinutes);

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _options = jwtOptions.Value;
    }

    public string GenerateAccessToken(Guid userId, string provider, IEnumerable<string>? roles = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("provider", provider),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (roles != null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(AccessTokenExpiration),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
