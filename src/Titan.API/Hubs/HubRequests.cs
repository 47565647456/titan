using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

// Request DTOs for SignalR hub methods that require validation.
// These enable FluentValidation to be used consistently with controllers.

#region Account Hub Requests

/// <summary>
/// Request to create a new character.
/// </summary>
public record CreateCharacterRequest(
    string SeasonId,
    string Name,
    CharacterRestrictions Restrictions = CharacterRestrictions.None);

/// <summary>
/// Request for operations requiring just an ID (cosmetics, achievements).
/// </summary>
public record IdRequest(string Id);

#endregion

#region Character Hub Requests

/// <summary>
/// Request for character operations in a specific season.
/// </summary>
public record CharacterSeasonRequest(Guid CharacterId, string SeasonId);

/// <summary>
/// Request to add experience to a character.
/// </summary>
public record AddExperienceRequest(Guid CharacterId, string SeasonId, long Amount);

/// <summary>
/// Request to set a character stat.
/// </summary>
public record SetStatRequest(Guid CharacterId, string SeasonId, string StatName, int Value);

/// <summary>
/// Request to update challenge progress.
/// </summary>
public record UpdateChallengeRequest(Guid CharacterId, string SeasonId, string ChallengeId, int Progress);

#endregion

#region Season Hub Requests

/// <summary>
/// Request to create a new season (admin only).
/// </summary>
public record CreateSeasonHubRequest(
    string SeasonId,
    string Name,
    SeasonType Type,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate = null,
    SeasonStatus Status = SeasonStatus.Upcoming,
    string? MigrationTargetId = null,
    Dictionary<string, string>? Modifiers = null,
    bool IsVoid = false);

/// <summary>
/// Request to start migration for a season.
/// </summary>
public record StartMigrationRequest(string SeasonId, string? TargetSeasonId = null);

#endregion

#region Inventory Hub Requests

/// <summary>
/// Request to move an item in the bag.
/// </summary>
public record MoveBagItemRequest(Guid CharacterId, string SeasonId, Guid ItemId, int NewX, int NewY);

/// <summary>
/// Request to equip an item.
/// </summary>
public record EquipRequest(Guid CharacterId, string SeasonId, Guid BagItemId, Titan.Abstractions.Models.Items.EquipmentSlot Slot);

/// <summary>
/// Request to unequip an item.
/// </summary>
public record UnequipRequest(Guid CharacterId, string SeasonId, Titan.Abstractions.Models.Items.EquipmentSlot Slot);

#endregion

#region Trade Hub Requests

/// <summary>
/// Request to start a trade.
/// </summary>
public record StartTradeRequest(Guid MyCharacterId, Guid TargetCharacterId, string SeasonId);

/// <summary>
/// Request to add/remove an item from a trade.
/// </summary>
public record TradeItemRequest(Guid TradeId, Guid ItemId);

#endregion
