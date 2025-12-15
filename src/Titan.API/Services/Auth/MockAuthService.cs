namespace Titan.API.Services.Auth;

/// <summary>
/// Mock authentication service for testing.
/// Accepts tokens in format:
/// - "mock:{userId}" for regular user
/// - "mock:admin:{userId}" for admin user (admin role is granted by AuthHub, not here)
/// </summary>
public class MockAuthService : IAuthService
{
    public string ProviderName => "Mock";

    public Task<AuthResult> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthResult.Failed("Token is empty."));

        if (!token.StartsWith("mock:", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthResult.Failed("Invalid mock token format. Expected 'mock:{userId}' or 'mock:admin:{userId}'."));

        var parts = token.Split(':');
        
        // Handle both "mock:{guid}" and "mock:admin:{guid}" formats
        // The GUID is the last part in either case
        var guidPart = parts[^1];  // Last element
        
        if (!Guid.TryParse(guidPart, out var userId))
            return Task.FromResult(AuthResult.Failed("Invalid mock token. UserId must be a valid GUID."));

        return Task.FromResult(AuthResult.Authenticated(userId, ProviderName, guidPart));
    }
}
