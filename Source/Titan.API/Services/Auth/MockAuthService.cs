namespace Titan.API.Services.Auth;

/// <summary>
/// Mock authentication service for testing.
/// Accepts any token in format "mock:{userId}" or "mock:{userId}:{displayName}".
/// </summary>
public class MockAuthService : IAuthService
{
    public string ProviderName => "Mock";

    public Task<AuthResult> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthResult.Failed("Token is empty."));

        if (!token.StartsWith("mock:", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthResult.Failed("Invalid mock token format. Expected 'mock:{userId}'."));

        var parts = token.Split(':');
        if (parts.Length < 2 || !Guid.TryParse(parts[1], out var userId))
            return Task.FromResult(AuthResult.Failed("Invalid mock token. UserId must be a valid GUID."));

        return Task.FromResult(AuthResult.Authenticated(userId, ProviderName, parts[1]));
    }
}
