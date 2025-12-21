using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Titan.API.Config;

/// <summary>
/// Session-based authentication configuration options.
/// </summary>
public class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>
    /// Session lifetime in minutes. Default: 60 minutes (1 hour).
    /// </summary>
    [Range(5, 1440)]
    public int SessionLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Sliding expiration in minutes. Activity extends session by this amount.
    /// Must be less than SessionLifetimeMinutes. Default: 15 minutes.
    /// </summary>
    [Range(1, 60)]
    public int SlidingExpirationMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum concurrent sessions per user. 0 = unlimited.
    /// When limit is reached, oldest session is invalidated.
    /// Default: 5.
    /// </summary>
    [Range(0, 100)]
    public int MaxSessionsPerUser { get; set; } = 5;

    /// <summary>
    /// Admin session lifetime in minutes. Shorter for security.
    /// Default: 30 minutes.
    /// </summary>
    [Range(5, 120)]
    public int AdminSessionLifetimeMinutes { get; set; } = 30;

    /// <summary>
    /// Redis key prefix for session storage.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string KeyPrefix { get; set; } = "session";
}

/// <summary>
/// Validates SessionOptions cross-property constraints.
/// </summary>
public class SessionOptionsValidator : IValidateOptions<SessionOptions>
{
    public ValidateOptionsResult Validate(string? name, SessionOptions options)
    {
        if (options.SlidingExpirationMinutes >= options.SessionLifetimeMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"SlidingExpirationMinutes ({options.SlidingExpirationMinutes}) must be less than " +
                $"SessionLifetimeMinutes ({options.SessionLifetimeMinutes})");
        }

        if (options.SlidingExpirationMinutes >= options.AdminSessionLifetimeMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"SlidingExpirationMinutes ({options.SlidingExpirationMinutes}) must be less than " +
                $"AdminSessionLifetimeMinutes ({options.AdminSessionLifetimeMinutes})");
        }

        return ValidateOptionsResult.Success;
    }
}
