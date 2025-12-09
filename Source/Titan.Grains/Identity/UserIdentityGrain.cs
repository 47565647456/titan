using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

public class UserIdentityGrainState
{
    public UserIdentity? Identity { get; set; }
}

public class UserIdentityGrain : Grain, IUserIdentityGrain
{
    private readonly IPersistentState<UserIdentityGrainState> _state;

    public UserIdentityGrain(
        [PersistentState("userIdentity", "OrleansStorage")] IPersistentState<UserIdentityGrainState> state)
    {
        _state = state;
    }

    public Task<UserIdentity> GetIdentityAsync()
    {
        if (_state.State.Identity == null)
        {
            _state.State.Identity = new UserIdentity { UserId = this.GetPrimaryKey() };
        }
        return Task.FromResult(_state.State.Identity);
    }

    public async Task LinkProviderAsync(string providerName, string externalId)
    {
        var identity = await GetIdentityAsync();
        
        // Avoid duplicates
        if (identity.LinkedProviders.Any(p => p.ProviderName == providerName))
            return;

        var newProvider = new LinkedProvider
        {
            ProviderName = providerName,
            ExternalId = externalId,
            LinkedAt = DateTimeOffset.UtcNow
        };

        _state.State.Identity = identity with
        {
            LinkedProviders = identity.LinkedProviders.Append(newProvider).ToList()
        };

        await _state.WriteStateAsync();
    }

    public async Task<bool> HasProviderAsync(string providerName)
    {
        var identity = await GetIdentityAsync();
        return identity.LinkedProviders.Any(p => p.ProviderName == providerName);
    }
}
