namespace Titan.API.Config;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int PermitLimit { get; set; } = 100;
    public double WindowMinutes { get; set; } = 1.0;
}
