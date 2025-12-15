namespace Titan.API.Config;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Enable or disable rate limiting. Set to false for load testing.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    public int PermitLimit { get; set; } = 100;
    public double WindowMinutes { get; set; } = 1.0;
}
