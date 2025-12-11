using System.ComponentModel.DataAnnotations;

namespace Titan.API.Config;

/// <summary>
/// Configuration options for Epic Online Services (EOS) Connect authentication.
/// </summary>
public class EosOptions
{
    public const string SectionName = "Eos";

    /// <summary>
    /// Your EOS Client ID from the Epic Developer Portal.
    /// Required for validating the 'aud' (audience) claim in ID Tokens.
    /// </summary>
    [Required]
    public required string ClientId { get; set; }

    /// <summary>
    /// Your EOS Deployment ID. Used for additional validation if needed.
    /// </summary>
    public string? DeploymentId { get; set; }

    /// <summary>
    /// Expected issuer prefix for ID Tokens.
    /// Default: https://api.epicgames.dev
    /// </summary>
    public string IssuerPrefix { get; set; } = "https://api.epicgames.dev";

    /// <summary>
    /// JWKS endpoint for fetching public keys to validate ID Token signatures.
    /// Default: https://api.epicgames.dev/auth/v1/oauth/jwks
    /// </summary>
    public string JwksUri { get; set; } = "https://api.epicgames.dev/auth/v1/oauth/jwks";

    /// <summary>
    /// How long to cache JWKS keys before refreshing.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan JwksCacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Clock skew tolerance for token expiration validation.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
