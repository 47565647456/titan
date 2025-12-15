using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for user profile data (Display Name, Settings).
/// Key: UserId (Guid)
/// </summary>
public interface IUserProfileGrain : IGrainWithGuidKey
{
    Task<UserProfile> GetProfileAsync();
    Task UpdateProfileAsync(UserProfile profile);
}
