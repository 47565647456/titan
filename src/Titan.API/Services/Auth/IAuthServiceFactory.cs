namespace Titan.API.Services.Auth;

/// <summary>
/// Factory for resolving authentication services by provider name.
/// Supports multiple authentication providers (EOS, Mock, etc.).
/// </summary>
public interface IAuthServiceFactory
{
    /// <summary>
    /// Gets an authentication service by provider name.
    /// </summary>
    /// <param name="providerName">The provider name (e.g., "EOS", "Mock").</param>
    /// <returns>The authentication service for the specified provider.</returns>
    /// <exception cref="ArgumentException">Thrown if the provider is not registered.</exception>
    IAuthService GetService(string providerName);

    /// <summary>
    /// Gets the default authentication service.
    /// In production: EOS. In development: Mock (if available).
    /// </summary>
    IAuthService GetDefaultService();

    /// <summary>
    /// Gets all registered provider names.
    /// </summary>
    IEnumerable<string> GetProviderNames();

    /// <summary>
    /// Checks if a provider is registered.
    /// </summary>
    bool HasProvider(string providerName);
}
