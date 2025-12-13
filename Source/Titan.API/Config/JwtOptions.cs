using System.ComponentModel.DataAnnotations;

namespace Titan.API.Config;

/// <summary>
/// JWT and token configuration options.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The JWT signing key. Must be at least 32 characters.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The JWT issuer claim.
    /// </summary>
    public string Issuer { get; set; } = "Titan";

    /// <summary>
    /// The JWT audience claim.
    /// </summary>
    public string Audience { get; set; } = "Titan";

    /// <summary>
    /// Access token expiration in minutes. Default: 15 minutes.
    /// </summary>
    [Range(1, 1440)] // 1 minute to 24 hours
    public int AccessTokenExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token expiration in minutes. Default: 7 days (10080 minutes).
    /// </summary>
    [Range(60, 43200)] // 1 hour to 30 days
    public int RefreshTokenExpirationMinutes { get; set; } = 10080;
}
