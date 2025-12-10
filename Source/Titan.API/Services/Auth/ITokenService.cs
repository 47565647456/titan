namespace Titan.API.Services.Auth;

/// <summary>
/// Service for generating JWT tokens for authenticated users.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT token for the specified user.
    /// </summary>
    /// <param name="userId">The user's internal ID.</param>
    /// <param name="provider">The authentication provider (e.g., "EOS", "Mock").</param>
    /// <param name="roles">Optional roles to include in the token.</param>
    /// <returns>A signed JWT token string.</returns>
    string GenerateToken(Guid userId, string provider, IEnumerable<string>? roles = null);
}
