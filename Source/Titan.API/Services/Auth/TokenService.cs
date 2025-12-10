using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Titan.API.Services.Auth;

/// <summary>
/// JWT token generation service.
/// Generates signed tokens for authenticated users.
/// </summary>
public class TokenService : ITokenService
{
    private readonly string _key;
    private readonly string _issuer;
    private readonly TimeSpan _expiration;

    public TokenService(IConfiguration configuration)
    {
        _key = configuration["Jwt:Key"] 
            ?? throw new InvalidOperationException("Jwt:Key must be configured.");
        _issuer = configuration["Jwt:Issuer"] ?? "Titan";
        _expiration = TimeSpan.FromHours(
            configuration.GetValue<int>("Jwt:ExpirationHours", 24));
    }

    public string GenerateToken(Guid userId, string provider, IEnumerable<string>? roles = null)
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

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.Add(_expiration),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
