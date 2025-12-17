using Microsoft.AspNetCore.SignalR;

namespace Titan.API.Services;

/// <summary>
/// Validation helpers for SignalR hubs.
/// Throws HubException with user-friendly messages for invalid input.
/// </summary>
public static class HubValidation
{
    public const int MaxIdLength = 100;
    public const int MaxNameLength = 200;

    /// <summary>
    /// Validates an ID parameter (seasonId, baseTypeId, etc.)
    /// </summary>
    public static void ValidateId(string? id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new HubException($"{parameterName} is required");
        if (id.Length > MaxIdLength)
            throw new HubException($"{parameterName} exceeds maximum length of {MaxIdLength}");
    }

    /// <summary>
    /// Validates a name parameter.
    /// </summary>
    public static void ValidateName(string? name, string parameterName, int maxLength = MaxNameLength)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new HubException($"{parameterName} is required");
        if (name.Length > maxLength)
            throw new HubException($"{parameterName} exceeds maximum length of {maxLength}");
    }

    /// <summary>
    /// Validates a positive integer value.
    /// </summary>
    public static void ValidatePositive(long value, string parameterName)
    {
        if (value < 0)
            throw new HubException($"{parameterName} cannot be negative");
    }
}
