using MemoryPack;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

[GenerateSerializer]
[MemoryPackable]
public partial class AccountGrainState
{
    [Id(0), MemoryPackOrder(0)] public Account? Account { get; set; }
    [Id(1), MemoryPackOrder(1)] public List<CharacterSummary> Characters { get; set; } = new();
}

/// <summary>
/// Global account grain - persists across all seasons.
/// </summary>
public class AccountGrain : Grain, IAccountGrain
{
    private readonly IPersistentState<AccountGrainState> _state;
    private readonly IGrainFactory _grainFactory;

    public AccountGrain(
        [PersistentState("account", "OrleansStorage")] IPersistentState<AccountGrainState> state,
        IGrainFactory grainFactory)
    {
        _state = state;
        _grainFactory = grainFactory;
    }

    public async Task<Account> GetAccountAsync()
    {
        if (_state.State.Account == null)
        {
            _state.State.Account = new Account
            {
                AccountId = this.GetPrimaryKey()
            };
            // Persist new accounts so they appear in the database
            await _state.WriteStateAsync();
        }
        return _state.State.Account;
    }

    public Task<IReadOnlyList<CharacterSummary>> GetCharactersAsync()
    {
        return Task.FromResult<IReadOnlyList<CharacterSummary>>(_state.State.Characters);
    }

    public async Task<CharacterSummary> CreateCharacterAsync(string seasonId, string name, CharacterRestrictions restrictions)
    {
        // Ensure account exists
        await GetAccountAsync();

        // Validate season exists and is active
        var seasonRegistry = _grainFactory.GetGrain<ISeasonRegistryGrain>("default");
        var season = await seasonRegistry.GetSeasonAsync(seasonId);
        
        if (season == null)
            throw new InvalidOperationException($"Season '{seasonId}' not found.");
        
        if (season.Status != SeasonStatus.Active && season.Status != SeasonStatus.Upcoming)
            throw new InvalidOperationException($"Cannot create characters in season '{seasonId}' (status: {season.Status}).");

        // Create the character
        var characterId = Guid.NewGuid();
        var characterGrain = _grainFactory.GetGrain<ICharacterGrain>(characterId, seasonId);
        var character = await characterGrain.InitializeAsync(this.GetPrimaryKey(), name, restrictions);

        // Add to our character list
        var summary = new CharacterSummary
        {
            CharacterId = characterId,
            SeasonId = seasonId,
            Name = name,
            Level = character.Level,
            Restrictions = restrictions,
            IsDead = false,
            CreatedAt = character.CreatedAt
        };

        _state.State.Characters.Add(summary);
        await _state.WriteStateAsync();

        return summary;
    }

    public async Task UpdateCharacterSummaryAsync(CharacterSummary summary)
    {
        var existing = _state.State.Characters.FindIndex(c => c.CharacterId == summary.CharacterId);
        if (existing >= 0)
        {
            _state.State.Characters[existing] = summary;
        }
        else
        {
            _state.State.Characters.Add(summary);
        }
        await _state.WriteStateAsync();
    }

    public async Task UnlockCosmeticAsync(string cosmeticId)
    {
        var account = await GetAccountAsync();
        if (!account.UnlockedCosmetics.Contains(cosmeticId))
        {
            _state.State.Account = account with
            {
                UnlockedCosmetics = account.UnlockedCosmetics.Append(cosmeticId).ToList()
            };
            await _state.WriteStateAsync();
        }
    }

    public async Task UnlockAchievementAsync(string achievementId)
    {
        var account = await GetAccountAsync();
        if (!account.UnlockedAchievements.Contains(achievementId))
        {
            _state.State.Account = account with
            {
                UnlockedAchievements = account.UnlockedAchievements.Append(achievementId).ToList()
            };
            await _state.WriteStateAsync();
        }
    }

    public async Task<bool> HasCosmeticAsync(string cosmeticId)
    {
        var account = await GetAccountAsync();
        return account.UnlockedCosmetics.Contains(cosmeticId);
    }

    public async Task<bool> HasAchievementAsync(string achievementId)
    {
        var account = await GetAccountAsync();
        return account.UnlockedAchievements.Contains(achievementId);
    }
}
