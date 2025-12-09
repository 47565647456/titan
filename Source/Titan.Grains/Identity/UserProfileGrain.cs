using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

public class UserProfileGrainState
{
    public UserProfile Profile { get; set; } = new();
}

public class UserProfileGrain : Grain, IUserProfileGrain
{
    private readonly IPersistentState<UserProfileGrainState> _state;

    public UserProfileGrain(
        [PersistentState("userProfile", "OrleansStorage")] IPersistentState<UserProfileGrainState> state)
    {
        _state = state;
    }

    public Task<UserProfile> GetProfileAsync()
    {
        return Task.FromResult(_state.State.Profile);
    }

    public async Task UpdateProfileAsync(UserProfile profile)
    {
        _state.State.Profile = profile;
        await _state.WriteStateAsync();
    }
}
