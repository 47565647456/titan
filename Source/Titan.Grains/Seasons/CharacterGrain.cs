using MemoryPack;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Seasons;

[GenerateSerializer]
[MemoryPackable]
public partial class CharacterGrainState
{
    [Id(0), MemoryPackOrder(0)] public Character? Character { get; set; }
    [Id(1), MemoryPackOrder(1)] public List<ChallengeProgress> ChallengeProgress { get; set; } = new();
    [Id(2), MemoryPackOrder(2)] public List<CharacterHistoryEntry> History { get; set; } = new();
}

/// <summary>
/// Per-season character grain with compound key (CharacterId, SeasonId).
/// </summary>
public class CharacterGrain : Grain, ICharacterGrain
{
    private readonly IPersistentState<CharacterGrainState> _state;
    private readonly IGrainFactory _grainFactory;

    public CharacterGrain(
        [PersistentState("character", "OrleansStorage")] IPersistentState<CharacterGrainState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    private string GetSeasonId()
    {
        this.GetPrimaryKey(out var seasonId);
        return seasonId!;
    }

    public Task<Character> GetCharacterAsync()
    {
        if (_state.State.Character == null)
            throw new InvalidOperationException("Character not initialized.");
        return Task.FromResult(_state.State.Character);
    }

    public async Task<Character> InitializeAsync(Guid accountId, string name, CharacterRestrictions restrictions)
    {
        if (_state.State.Character != null)
            throw new InvalidOperationException("Character already initialized.");

        var character = new Character
        {
            CharacterId = this.GetPrimaryKey(out _),
            AccountId = accountId,
            SeasonId = GetSeasonId(),
            Name = name,
            Restrictions = restrictions,
            Level = 1,
            Experience = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _state.State.Character = character;
        
        // Record creation in history
        _state.State.History.Add(new CharacterHistoryEntry
        {
            EventType = CharacterEventTypes.Created,
            Description = $"Character '{name}' created in season '{GetSeasonId()}'",
            Data = new Dictionary<string, string>
            {
                ["seasonId"] = GetSeasonId(),
                ["restrictions"] = restrictions.ToString()
            }
        });
        
        await _state.WriteStateAsync();

        return character;
    }

    public async Task<Character> AddExperienceAsync(long amount)
    {
        var character = await GetCharacterAsync();
        
        // Simple leveling formula - can be customized
        var newExp = character.Experience + amount;
        var newLevel = CalculateLevel(newExp);

        _state.State.Character = character with
        {
            Experience = newExp,
            Level = newLevel
        };
        await _state.WriteStateAsync();

        // Update account summary if level changed
        if (newLevel != character.Level)
        {
            await UpdateAccountSummaryAsync();
        }

        return _state.State.Character;
    }

    private int CalculateLevel(long experience)
    {
        // Simple formula: level = sqrt(exp / 100) + 1
        return (int)Math.Floor(Math.Sqrt(experience / 100.0)) + 1;
    }

    public async Task<Character> SetStatAsync(string statName, int value)
    {
        var character = await GetCharacterAsync();
        var newStats = new Dictionary<string, int>(character.Stats)
        {
            [statName] = value
        };

        _state.State.Character = character with { Stats = newStats };
        await _state.WriteStateAsync();

        return _state.State.Character;
    }

    public Task<IReadOnlyList<ChallengeProgress>> GetChallengeProgressAsync()
    {
        return Task.FromResult<IReadOnlyList<ChallengeProgress>>(_state.State.ChallengeProgress);
    }

    public async Task UpdateChallengeProgressAsync(string challengeId, int progress)
    {
        var existing = _state.State.ChallengeProgress.FindIndex(c => c.ChallengeId == challengeId);
        
        if (existing >= 0)
        {
            var current = _state.State.ChallengeProgress[existing];
            _state.State.ChallengeProgress[existing] = current with
            {
                CurrentProgress = progress,
                IsCompleted = progress >= current.CurrentProgress, // Simplified - would check against challenge definition
                CompletedAt = progress >= current.CurrentProgress ? DateTimeOffset.UtcNow : current.CompletedAt
            };
        }
        else
        {
            _state.State.ChallengeProgress.Add(new ChallengeProgress
            {
                ChallengeId = challengeId,
                CurrentProgress = progress
            });
        }

        await _state.WriteStateAsync();
    }

    public async Task<Character> DieAsync()
    {
        var character = await GetCharacterAsync();

        if (!character.Restrictions.HasFlag(CharacterRestrictions.Hardcore))
            throw new InvalidOperationException("Only Hardcore characters can use DieAsync.");

        if (character.IsDead)
            throw new InvalidOperationException("Character is already dead.");

        // Mark as dead
        _state.State.Character = character with { IsDead = true };
        
        // Record death in history
        _state.State.History.Add(new CharacterHistoryEntry
        {
            EventType = CharacterEventTypes.Died,
            Description = $"Hardcore character '{character.Name}' died in season '{character.SeasonId}'",
            Data = new Dictionary<string, string>
            {
                ["seasonId"] = character.SeasonId,
                ["level"] = character.Level.ToString()
            }
        });
        
        await _state.WriteStateAsync();

        // Update account summary
        await UpdateAccountSummaryAsync();

        // Get the season to find migration target
        var seasonRegistry = _grainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var season = await seasonRegistry.GetSeasonAsync(character.SeasonId);

        if (season != null && season.Type == SeasonType.Temporary)
        {
            // Void leagues do not migrate on death - character stays dead in the void season
            if (season.IsVoid)
            {
                return _state.State.Character;
            }

            // Migrate to permanent standard season
            await MigrateToSeasonAsync("standard");
        }

        return _state.State.Character;
    }

    public async Task<Character> MigrateToSeasonAsync(string targetSeasonId)
    {
        var character = await GetCharacterAsync();

        if (character.IsMigrated)
            throw new InvalidOperationException("Character has already been migrated.");

        // Create the character in the target season
        var newCharacterId = character.CharacterId; // Keep same ID for continuity
        var targetCharacter = _grainFactory.GetGrain<ICharacterGrain>(newCharacterId, targetSeasonId);

        // Determine new restrictions (remove Hardcore if dead)
        var newRestrictions = character.Restrictions;
        if (character.IsDead)
        {
            newRestrictions &= ~CharacterRestrictions.Hardcore;
        }

        // Initialize in target season
        await targetCharacter.InitializeAsync(character.AccountId, character.Name, newRestrictions);

        // Copy stats and progress
        foreach (var stat in character.Stats)
        {
            await targetCharacter.SetStatAsync(stat.Key, stat.Value);
        }

        // Mark this character as migrated
        _state.State.Character = character with
        {
            IsMigrated = true,
            OriginalSeasonId = character.SeasonId
        };
        
        // Record migration in history
        _state.State.History.Add(new CharacterHistoryEntry
        {
            EventType = CharacterEventTypes.Migrated,
            Description = $"Character migrated from '{character.SeasonId}' to '{targetSeasonId}'",
            Data = new Dictionary<string, string>
            {
                ["sourceSeasonId"] = character.SeasonId,
                ["targetSeasonId"] = targetSeasonId
            }
        });
        
        // Record restriction change if applicable
        if (newRestrictions != character.Restrictions)
        {
            _state.State.History.Add(new CharacterHistoryEntry
            {
                EventType = CharacterEventTypes.RestrictionsChanged,
                Description = $"Restrictions changed from '{character.Restrictions}' to '{newRestrictions}'",
                Data = new Dictionary<string, string>
                {
                    ["previousRestrictions"] = character.Restrictions.ToString(),
                    ["newRestrictions"] = newRestrictions.ToString(),
                    ["reason"] = "Migration after death"
                }
            });
        }
        
        await _state.WriteStateAsync();

        // Update account with new character in target season
        var accountGrain = _grainFactory.GetGrain<IAccountGrain>(character.AccountId);
        await accountGrain.UpdateCharacterSummaryAsync(new CharacterSummary
        {
            CharacterId = newCharacterId,
            SeasonId = targetSeasonId,
            Name = character.Name,
            Level = character.Level,
            Restrictions = newRestrictions,
            IsDead = false, // Reset dead flag in new season
            CreatedAt = character.CreatedAt
        });

        return _state.State.Character;
    }

    private async Task UpdateAccountSummaryAsync()
    {
        var character = _state.State.Character!;
        var accountGrain = _grainFactory.GetGrain<IAccountGrain>(character.AccountId);
        await accountGrain.UpdateCharacterSummaryAsync(new CharacterSummary
        {
            CharacterId = character.CharacterId,
            SeasonId = character.SeasonId,
            Name = character.Name,
            Level = character.Level,
            Restrictions = character.Restrictions,
            IsDead = character.IsDead,
            CreatedAt = character.CreatedAt
        });
    }

    #region History

    public Task<IReadOnlyList<CharacterHistoryEntry>> GetHistoryAsync()
    {
        return Task.FromResult<IReadOnlyList<CharacterHistoryEntry>>(
            _state.State.History.OrderBy(h => h.Timestamp).ToList());
    }

    public async Task AddHistoryEntryAsync(string eventType, string description, Dictionary<string, string>? data = null)
    {
        if (_state.State.Character == null)
            throw new InvalidOperationException("Character not initialized.");

        _state.State.History.Add(new CharacterHistoryEntry
        {
            EventType = eventType,
            Description = description,
            Data = data
        });

        await _state.WriteStateAsync();
    }

    #endregion
}
