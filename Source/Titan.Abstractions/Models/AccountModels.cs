using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents a player's account - persists across all seasons.
/// Accounts hold global unlocks like cosmetics and achievements.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record Account
{
    /// <summary>
    /// Unique account identifier (same as user identity).
    /// </summary>
    [Id(0), MemoryPackOrder(0)] public required Guid AccountId { get; init; }

    /// <summary>
    /// When the account was created.
    /// </summary>
    [Id(1), MemoryPackOrder(1)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Cosmetics unlocked globally (persists across seasons).
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public List<string> UnlockedCosmetics { get; init; } = [];

    /// <summary>
    /// Achievements unlocked globally (persists across seasons).
    /// </summary>
    [Id(3), MemoryPackOrder(3)] public List<string> UnlockedAchievements { get; init; } = [];
}

/// <summary>
/// Summary of a character for account-level queries.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record CharacterSummary
{
    [Id(0), MemoryPackOrder(0)] public required Guid CharacterId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string SeasonId { get; init; }
    [Id(2), MemoryPackOrder(2)] public required string Name { get; init; }
    [Id(3), MemoryPackOrder(3)] public int Level { get; init; }
    [Id(4), MemoryPackOrder(4)] public CharacterRestrictions Restrictions { get; init; }
    [Id(5), MemoryPackOrder(5)] public bool IsDead { get; init; }
    [Id(6), MemoryPackOrder(6)] public DateTimeOffset CreatedAt { get; init; }
}
