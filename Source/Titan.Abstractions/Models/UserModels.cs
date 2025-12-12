using MemoryPack;
using Orleans;

namespace Titan.Abstractions.Models;

/// <summary>
/// Represents a user identity linked to external providers.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record UserIdentity
{
    [Id(0), MemoryPackOrder(0)] public required Guid UserId { get; init; }
    [Id(1), MemoryPackOrder(1)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    [Id(2), MemoryPackOrder(2)] public List<LinkedProvider> LinkedProviders { get; init; } = new();
}

/// <summary>
/// A linked external identity (EOS, Steam, etc).
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record LinkedProvider
{
    [Id(0), MemoryPackOrder(0)] public required string ProviderName { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string ExternalId { get; init; }
    [Id(2), MemoryPackOrder(2)] public DateTimeOffset LinkedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// User profile data (Display Name, Settings, Avatar).
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record UserProfile
{
    [Id(0), MemoryPackOrder(0)] public string? DisplayName { get; init; }
    [Id(1), MemoryPackOrder(1)] public string? AvatarUrl { get; init; }
    /// <summary>
    /// User settings as JSON-serialized key-value pairs for extensibility.
    /// </summary>
    [Id(2), MemoryPackOrder(2)] public Dictionary<string, string>? Settings { get; init; }
}

/// <summary>
/// Social graph entry.
/// </summary>
[GenerateSerializer]
[MemoryPackable]
public partial record SocialRelation
{
    [Id(0), MemoryPackOrder(0)] public required Guid TargetUserId { get; init; }
    [Id(1), MemoryPackOrder(1)] public required string RelationType { get; init; }
    [Id(2), MemoryPackOrder(2)] public DateTimeOffset Since { get; init; } = DateTimeOffset.UtcNow;
}
