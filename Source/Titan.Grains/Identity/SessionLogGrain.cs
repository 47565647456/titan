using MemoryPack;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

[GenerateSerializer]
[MemoryPackable]
public partial class SessionLogGrainState
{
    [Id(0), MemoryPackOrder(0)] public SessionLog? CurrentSession { get; set; }
    [Id(1), MemoryPackOrder(1)] public List<SessionLog> SessionHistory { get; set; } = new();
}

/// <summary>
/// Persisted session logging grain.
/// Tracks login/logout history for analytics and security auditing.
/// </summary>
public class SessionLogGrain : Grain, ISessionLogGrain
{
    private readonly IPersistentState<SessionLogGrainState> _state;

    public SessionLogGrain(
        [PersistentState("sessionLog", "OrleansStorage")] IPersistentState<SessionLogGrainState> state)
    {
        _state = state;
    }

    public async Task<Guid> StartSessionAsync(string? ipAddress)
    {
        var sessionId = Guid.NewGuid();
        _state.State.CurrentSession = new SessionLog
        {
            SessionId = sessionId,
            UserId = this.GetPrimaryKey(),
            LoginAt = DateTimeOffset.UtcNow,
            IpAddress = ipAddress
        };
        _state.State.SessionHistory.Add(_state.State.CurrentSession);
        await _state.WriteStateAsync();
        return sessionId;
    }

    public async Task EndSessionAsync()
    {
        if (_state.State.CurrentSession != null)
        {
            var loginAt = _state.State.CurrentSession.LoginAt;
            _state.State.CurrentSession = _state.State.CurrentSession with
            {
                LogoutAt = DateTimeOffset.UtcNow,
                Duration = DateTimeOffset.UtcNow - loginAt
            };

            // Update the history entry
            var idx = _state.State.SessionHistory.FindIndex(
                s => s.SessionId == _state.State.CurrentSession.SessionId);
            if (idx >= 0)
                _state.State.SessionHistory[idx] = _state.State.CurrentSession;

            await _state.WriteStateAsync();
            _state.State.CurrentSession = null;
        }
    }

    public Task<IReadOnlyList<SessionLog>> GetRecentSessionsAsync(int count = 10)
    {
        var recent = _state.State.SessionHistory
            .OrderByDescending(s => s.LoginAt)
            .Take(count)
            .ToList();
        return Task.FromResult<IReadOnlyList<SessionLog>>(recent);
    }
}
