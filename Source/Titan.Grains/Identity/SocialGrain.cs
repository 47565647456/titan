using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

public class SocialGrainState
{
    public List<SocialRelation> Relations { get; set; } = new();
}

public class SocialGrain : Grain, ISocialGrain
{
    private readonly IPersistentState<SocialGrainState> _state;

    public SocialGrain(
        [PersistentState("social", "OrleansStorage")] IPersistentState<SocialGrainState> state)
    {
        _state = state;
    }

    public Task<List<SocialRelation>> GetRelationsAsync()
    {
        return Task.FromResult(_state.State.Relations);
    }

    public async Task AddRelationAsync(Guid targetUserId, string relationType)
    {
        // Avoid duplicates
        if (_state.State.Relations.Any(r => r.TargetUserId == targetUserId && r.RelationType == relationType))
            return;

        _state.State.Relations.Add(new SocialRelation
        {
            TargetUserId = targetUserId,
            RelationType = relationType,
            Since = DateTimeOffset.UtcNow
        });

        await _state.WriteStateAsync();
    }

    public async Task RemoveRelationAsync(Guid targetUserId)
    {
        _state.State.Relations.RemoveAll(r => r.TargetUserId == targetUserId);
        await _state.WriteStateAsync();
    }

    public Task<bool> HasRelationAsync(Guid targetUserId, string relationType)
    {
        var exists = _state.State.Relations.Any(r => r.TargetUserId == targetUserId && r.RelationType == relationType);
        return Task.FromResult(exists);
    }
}
