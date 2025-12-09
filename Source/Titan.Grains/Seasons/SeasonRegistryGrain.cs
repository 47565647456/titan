using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Seasons;

public class SeasonRegistryGrainState
{
    public Dictionary<string, Season> Seasons { get; set; } = new();
}

/// <summary>
/// Singleton grain managing all seasons.
/// </summary>
public class SeasonRegistryGrain : Grain, ISeasonRegistryGrain
{
    private readonly IPersistentState<SeasonRegistryGrainState> _state;

    public SeasonRegistryGrain(
        [PersistentState("seasonRegistry", "OrleansStorage")] IPersistentState<SeasonRegistryGrainState> state)
    {
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Ensure the permanent "standard" season always exists
        if (!_state.State.Seasons.ContainsKey("standard"))
        {
            _state.State.Seasons["standard"] = new Season
            {
                SeasonId = "standard",
                Name = "Standard",
                Type = SeasonType.Permanent,
                Status = SeasonStatus.Active,
                StartDate = DateTimeOffset.MinValue,
                MigrationTargetId = "standard" // Self-reference, never migrates
            };
            await _state.WriteStateAsync();
        }
    }

    public Task<Season?> GetCurrentSeasonAsync()
    {
        // Return the active temporary season (if any)
        var current = _state.State.Seasons.Values
            .FirstOrDefault(s => s.Type == SeasonType.Temporary && s.Status == SeasonStatus.Active);
        return Task.FromResult(current);
    }

    public Task<Season?> GetSeasonAsync(string seasonId)
    {
        _state.State.Seasons.TryGetValue(seasonId, out var season);
        return Task.FromResult(season);
    }

    public Task<IReadOnlyList<Season>> GetAllSeasonsAsync()
    {
        return Task.FromResult<IReadOnlyList<Season>>(_state.State.Seasons.Values.ToList());
    }

    public async Task<Season> CreateSeasonAsync(Season season)
    {
        if (_state.State.Seasons.ContainsKey(season.SeasonId))
            throw new InvalidOperationException($"Season '{season.SeasonId}' already exists.");

        _state.State.Seasons[season.SeasonId] = season;
        await _state.WriteStateAsync();
        return season;
    }

    public async Task<Season> EndSeasonAsync(string seasonId)
    {
        if (!_state.State.Seasons.TryGetValue(seasonId, out var season))
            throw new InvalidOperationException($"Season '{seasonId}' not found.");

        if (season.Type == SeasonType.Permanent)
            throw new InvalidOperationException("Cannot end a permanent season.");

        if (season.Status == SeasonStatus.Ended || season.Status == SeasonStatus.Archived)
            throw new InvalidOperationException($"Season '{seasonId}' is already ended.");

        var endedSeason = season with
        {
            Status = SeasonStatus.Ended,
            EndDate = DateTimeOffset.UtcNow
        };

        _state.State.Seasons[seasonId] = endedSeason;
        await _state.WriteStateAsync();

        // Migration will be triggered separately by the migration grain
        return endedSeason;
    }

    public async Task<Season> UpdateSeasonStatusAsync(string seasonId, SeasonStatus status)
    {
        if (!_state.State.Seasons.TryGetValue(seasonId, out var season))
            throw new InvalidOperationException($"Season '{seasonId}' not found.");

        var updatedSeason = season with { Status = status };
        _state.State.Seasons[seasonId] = updatedSeason;
        await _state.WriteStateAsync();

        return updatedSeason;
    }
}
