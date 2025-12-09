using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing user identity and linked external providers.
/// Key: UserId (Guid)
/// </summary>
public interface IUserIdentityGrain : IGrainWithGuidKey
{
    /// <summary>
    /// Gets or creates the user identity. If no provider is linked, returns null-ish identity.
    /// </summary>
    Task<UserIdentity> GetIdentityAsync();

    /// <summary>
    /// Links an external provider to this user.
    /// </summary>
    Task LinkProviderAsync(string providerName, string externalId);

    /// <summary>
    /// Validates if a provider is linked.
    /// </summary>
    Task<bool> HasProviderAsync(string providerName);
}
