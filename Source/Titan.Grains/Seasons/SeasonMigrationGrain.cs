using MemoryPack;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Seasons;

[GenerateSerializer]
[MemoryPackable]
public partial class SeasonMigrationGrainState
{
    [Id(0), MemoryPackOrder(0)] public MigrationStatus Status { get; set; } = null!;
    [Id(1), MemoryPackOrder(1)] public List<Guid> PendingCharacterIds { get; set; } = [];
    [Id(2), MemoryPackOrder(2)] public List<Guid> MigratedCharacterIds { get; set; } = [];
}

/// <summary>
/// Orchestrates end-of-season migration for all characters in a season.
/// </summary>
public class SeasonMigrationGrain : Grain, ISeasonMigrationGrain
{
    private readonly IPersistentState<SeasonMigrationGrainState> _state;
    private readonly IGrainFactory _grainFactory;

    public SeasonMigrationGrain(
        [PersistentState("seasonMigration", "GlobalStorage")] IPersistentState<SeasonMigrationGrainState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    private string SeasonId => this.GetPrimaryKeyString();

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (_state.State.Status == null)
        {
            _state.State.Status = new MigrationStatus
            {
                SourceSeasonId = SeasonId,
                State = MigrationState.NotStarted
            };
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public Task<MigrationStatus> GetStatusAsync()
    {
        return Task.FromResult(_state.State.Status);
    }

    public async Task<MigrationStatus> StartMigrationAsync(string targetSeasonId)
    {
        if (_state.State.Status.State == MigrationState.InProgress)
            throw new InvalidOperationException("Migration is already in progress.");

        if (_state.State.Status.State == MigrationState.Completed)
            throw new InvalidOperationException("Migration has already completed.");

        // Get the season to verify it exists and has ended
        var registry = _grainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var season = await registry.GetSeasonAsync(SeasonId);
        
        if (season == null)
            throw new InvalidOperationException($"Season '{SeasonId}' not found.");
        
        if (season.Status != SeasonStatus.Ended && season.Status != SeasonStatus.Migrating)
            throw new InvalidOperationException($"Season '{SeasonId}' must be ended before migration. Current status: {season.Status}");

        // Void leagues do not migrate - characters and items are not preserved
        if (season.IsVoid)
            throw new InvalidOperationException($"Cannot migrate Void League '{SeasonId}'. Void league characters and items are not preserved.");

        // Update season status to migrating
        await registry.UpdateSeasonStatusAsync(SeasonId, SeasonStatus.Migrating);

        // Initialize migration status
        _state.State.Status = new MigrationStatus
        {
            SourceSeasonId = SeasonId,
            TargetSeasonId = targetSeasonId,
            State = MigrationState.InProgress,
            TotalCharacters = 0, // Will be updated as we discover characters
            MigratedCharacters = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        await _state.WriteStateAsync();

        // Note: In a real implementation, you'd queue characters for migration
        // via Orleans streams or a background worker. For simplicity, we're
        // just tracking state here.

        return _state.State.Status;
    }

    public async Task<MigrationStatus> MigrateCharacterAsync(Guid characterId)
    {
        if (_state.State.Status.State != MigrationState.InProgress)
            throw new InvalidOperationException("Migration is not in progress.");

        var targetSeasonId = _state.State.Status.TargetSeasonId;
        if (string.IsNullOrEmpty(targetSeasonId))
            throw new InvalidOperationException("Target season ID is not set.");

        try
        {
            // Get the character and migrate it
            var characterGrain = _grainFactory.GetGrain<ICharacterGrain>(characterId, SeasonId);
            await characterGrain.MigrateToSeasonAsync(targetSeasonId);

            // Track success
            _state.State.MigratedCharacterIds.Add(characterId);
            _state.State.Status = _state.State.Status with
            {
                MigratedCharacters = _state.State.MigratedCharacterIds.Count,
                TotalCharacters = Math.Max(_state.State.Status.TotalCharacters, _state.State.MigratedCharacterIds.Count + _state.State.PendingCharacterIds.Count)
            };
        }
        catch (Exception ex)
        {
            // Track failure
            _state.State.Status = _state.State.Status with
            {
                FailedCharacters = _state.State.Status.FailedCharacters + 1,
                Errors = _state.State.Status.Errors.Append($"Character {characterId}: {ex.Message}").ToList()
            };
        }

        await _state.WriteStateAsync();
        return _state.State.Status;
    }

    public async Task CancelMigrationAsync()
    {
        if (_state.State.Status.State != MigrationState.InProgress)
            throw new InvalidOperationException("No migration in progress to cancel.");

        _state.State.Status = _state.State.Status with
        {
            State = MigrationState.Cancelled,
            CompletedAt = DateTimeOffset.UtcNow
        };

        await _state.WriteStateAsync();
    }

    /// <summary>
    /// Call this when all characters have been processed to mark migration complete.
    /// </summary>
    public async Task CompleteMigrationAsync()
    {
        if (_state.State.Status.State != MigrationState.InProgress)
            return;

        var finalState = _state.State.Status.FailedCharacters > 0 
            ? MigrationState.Failed 
            : MigrationState.Completed;

        _state.State.Status = _state.State.Status with
        {
            State = finalState,
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Update season status to archived if migration completed successfully
        if (finalState == MigrationState.Completed)
        {
            var registry = _grainFactory.GetGrain<ISeasonRegistryGrain>("default");
            await registry.UpdateSeasonStatusAsync(SeasonId, SeasonStatus.Archived);
        }

        await _state.WriteStateAsync();
    }
}
