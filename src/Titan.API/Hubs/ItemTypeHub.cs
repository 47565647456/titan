using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Titan.API.Services;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for base type registry operations.
/// Read operations available to all authenticated users.
/// Write operations (Create/Update/Delete) require Admin role.
/// </summary>
[Authorize]
public class BaseTypeHub : TitanHubBase
{
    private readonly ILogger<BaseTypeHub> _logger;
    private readonly HubValidationService _validation;

    public BaseTypeHub(IClusterClient clusterClient, HubValidationService validation, ILogger<BaseTypeHub> logger)
        : base(clusterClient, logger)
    {
        _logger = logger;
        _validation = validation;
    }

    #region Subscriptions

    /// <summary>
    /// Join the base-types group to receive updates.
    /// </summary>
    public async Task JoinBaseTypesGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "base-types");
    }

    /// <summary>
    /// Leave the base-types group.
    /// </summary>
    public async Task LeaveBaseTypesGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "base-types");
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Get all registered base types.
    /// </summary>
    public async Task<IReadOnlyList<BaseType>> GetAll()
    {
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");
        return await registry.GetAllAsync();
    }

    /// <summary>
    /// Get a specific base type by ID.
    /// </summary>
    public async Task<BaseType?> Get(string baseTypeId)
    {
        await _validation.ValidateIdAsync(baseTypeId, nameof(baseTypeId));
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");
        return await registry.GetAsync(baseTypeId);
    }

    /// <summary>
    /// Get base types by category.
    /// </summary>
    public async Task<IReadOnlyList<BaseType>> GetByCategory(ItemCategory category)
    {
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");
        return await registry.GetByCategoryAsync(category);
    }

    /// <summary>
    /// Get base types by equipment slot.
    /// </summary>
    public async Task<IReadOnlyList<BaseType>> GetBySlot(EquipmentSlot slot)
    {
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");
        return await registry.GetBySlotAsync(slot);
    }

    /// <summary>
    /// Check if a base type exists.
    /// </summary>
    public async Task<bool> Exists(string baseTypeId)
    {
        await _validation.ValidateIdAsync(baseTypeId, nameof(baseTypeId));
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");
        return await registry.ExistsAsync(baseTypeId);
    }

    /// <summary>
    /// Create a new base type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<BaseType> Create(BaseType baseType)
    {
        await _validation.ValidateIdAsync(baseType.BaseTypeId, nameof(baseType.BaseTypeId));
        await _validation.ValidateNameAsync(baseType.Name, nameof(baseType.Name));

        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");

        if (await registry.ExistsAsync(baseType.BaseTypeId))
            throw new HubException($"Base type '{baseType.BaseTypeId}' already exists.");

        await registry.RegisterAsync(baseType);
        _logger.LogInformation("Base type '{BaseTypeId}' created via WebSocket", baseType.BaseTypeId);

        // Notify all subscribed clients
        await Clients.Group("base-types").SendAsync("BaseTypeCreated", baseType);

        return baseType;
    }

    /// <summary>
    /// Update an existing base type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<BaseType> Update(string baseTypeId, BaseType baseType)
    {
        await _validation.ValidateIdAsync(baseTypeId, nameof(baseTypeId));
        await _validation.ValidateNameAsync(baseType.Name, nameof(baseType.Name));

        if (baseType.BaseTypeId != baseTypeId)
            throw new HubException("BaseTypeId in request must match.");

        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(baseTypeId))
            throw new HubException($"Base type '{baseTypeId}' not found.");

        await registry.UpdateAsync(baseType);
        _logger.LogInformation("Base type '{BaseTypeId}' updated via WebSocket", baseTypeId);

        // Notify all subscribed clients
        await Clients.Group("base-types").SendAsync("BaseTypeUpdated", baseType);

        return baseType;
    }

    /// <summary>
    /// Delete a base type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task Delete(string baseTypeId)
    {
        await _validation.ValidateIdAsync(baseTypeId, nameof(baseTypeId));
        var registry = ClusterClient.GetGrain<IBaseTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(baseTypeId))
            throw new HubException($"Base type '{baseTypeId}' not found.");

        await registry.DeleteAsync(baseTypeId);
        _logger.LogInformation("Base type '{BaseTypeId}' deleted via WebSocket", baseTypeId);

        // Notify all subscribed clients
        await Clients.Group("base-types").SendAsync("BaseTypeDeleted", baseTypeId);
    }

    #endregion

    #region Server Push Helpers

    /// <summary>
    /// Broadcast base type created event (for server-side use).
    /// </summary>
    public static async Task NotifyBaseTypeCreated(IHubContext<BaseTypeHub> hubContext, BaseType baseType)
    {
        await hubContext.Clients.Group("base-types").SendAsync("BaseTypeCreated", baseType);
    }

    /// <summary>
    /// Broadcast base type updated event (for server-side use).
    /// </summary>
    public static async Task NotifyBaseTypeUpdated(IHubContext<BaseTypeHub> hubContext, BaseType baseType)
    {
        await hubContext.Clients.Group("base-types").SendAsync("BaseTypeUpdated", baseType);
    }

    /// <summary>
    /// Broadcast base type deleted event (for server-side use).
    /// </summary>
    public static async Task NotifyBaseTypeDeleted(IHubContext<BaseTypeHub> hubContext, string baseTypeId)
    {
        await hubContext.Clients.Group("base-types").SendAsync("BaseTypeDeleted", baseTypeId);
    }

    #endregion
}
