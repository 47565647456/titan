namespace Titan.Abstractions.RateLimiting;

/// <summary>
/// Specifies the rate limit policy for a controller or hub.
/// Can be applied to class (all methods) or individual methods (override class-level).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class RateLimitPolicyAttribute : Attribute
{
    /// <summary>
    /// The name of the rate limit policy to apply.
    /// </summary>
    public string PolicyName { get; }

    public RateLimitPolicyAttribute(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        PolicyName = policyName;
    }
}
