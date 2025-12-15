namespace Titan.API.Services.Auth;

/// <summary>
/// Result of an authentication attempt.
/// </summary>
public record AuthResult
{
    public bool Success { get; init; }
    public Guid? UserId { get; init; }
    public string? ProviderName { get; init; }
    public string? ExternalId { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthResult Authenticated(Guid userId, string providerName, string externalId)
        => new() { Success = true, UserId = userId, ProviderName = providerName, ExternalId = externalId };

    public static AuthResult Failed(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Abstraction for authentication services.
/// Supports multiple providers (EOS, Steam, Mock, etc).
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// The name of this authentication provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Validates a token and returns the authenticated user info.
    /// </summary>
    Task<AuthResult> ValidateTokenAsync(string token);
}
