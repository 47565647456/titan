using Orleans;
using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing social relationships (Friends, Blocks).
/// Key: UserId (Guid)
/// </summary>
public interface ISocialGrain : IGrainWithGuidKey
{
    Task<List<SocialRelation>> GetRelationsAsync();
    Task AddRelationAsync(Guid targetUserId, string relationType);
    Task RemoveRelationAsync(Guid targetUserId);
    Task<bool> HasRelationAsync(Guid targetUserId, string relationType);
}
