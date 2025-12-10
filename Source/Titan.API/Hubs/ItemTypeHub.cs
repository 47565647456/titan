using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.API.Hubs;

/// <summary>
/// WebSocket hub for item type registry operations.
/// Read operations available to all authenticated users.
/// Write operations (Create/Update/Delete) require Admin role.
/// </summary>
[Authorize]
public class ItemTypeHub : Hub
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<ItemTypeHub> _logger;

    public ItemTypeHub(IClusterClient clusterClient, ILogger<ItemTypeHub> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    #region Subscriptions

    /// <summary>
    /// Join the item-types group to receive updates.
    /// </summary>
    public async Task JoinItemTypesGroup()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "item-types");
    }

    /// <summary>
    /// Leave the item-types group.
    /// </summary>
    public async Task LeaveItemTypesGroup()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "item-types");
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Get all registered item types.
    /// </summary>
    public async Task<IReadOnlyList<ItemTypeDefinition>> GetAll()
    {
        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");
        return await registry.GetAllAsync();
    }

    /// <summary>
    /// Get a specific item type by ID.
    /// </summary>
    public async Task<ItemTypeDefinition?> Get(string itemTypeId)
    {
        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");
        return await registry.GetAsync(itemTypeId);
    }

    /// <summary>
    /// Check if an item type exists.
    /// </summary>
    public async Task<bool> Exists(string itemTypeId)
    {
        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");
        return await registry.ExistsAsync(itemTypeId);
    }

    /// <summary>
    /// Create a new item type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<ItemTypeDefinition> Create(ItemTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ItemTypeId))
            throw new HubException("ItemTypeId is required.");

        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new HubException("Name is required.");

        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");

        if (await registry.ExistsAsync(definition.ItemTypeId))
            throw new HubException($"Item type '{definition.ItemTypeId}' already exists.");

        await registry.RegisterAsync(definition);
        _logger.LogInformation("Item type '{ItemTypeId}' created via WebSocket", definition.ItemTypeId);

        // Notify all subscribed clients
        await Clients.Group("item-types").SendAsync("ItemTypeCreated", definition);

        return definition;
    }

    /// <summary>
    /// Update an existing item type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task<ItemTypeDefinition> Update(string itemTypeId, ItemTypeDefinition definition)
    {
        if (definition.ItemTypeId != itemTypeId)
            throw new HubException("ItemTypeId in request must match.");

        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(itemTypeId))
            throw new HubException($"Item type '{itemTypeId}' not found.");

        await registry.UpdateAsync(definition);
        _logger.LogInformation("Item type '{ItemTypeId}' updated via WebSocket", itemTypeId);

        // Notify all subscribed clients
        await Clients.Group("item-types").SendAsync("ItemTypeUpdated", definition);

        return definition;
    }

    /// <summary>
    /// Delete an item type. Broadcasts to all subscribed clients.
    /// Requires Admin role.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public async Task Delete(string itemTypeId)
    {
        var registry = _clusterClient.GetGrain<IItemTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(itemTypeId))
            throw new HubException($"Item type '{itemTypeId}' not found.");

        await registry.DeleteAsync(itemTypeId);
        _logger.LogInformation("Item type '{ItemTypeId}' deleted via WebSocket", itemTypeId);

        // Notify all subscribed clients
        await Clients.Group("item-types").SendAsync("ItemTypeDeleted", itemTypeId);
    }

    #endregion

    #region Server Push Helpers

    /// <summary>
    /// Broadcast item type created event (for server-side use).
    /// </summary>
    public static async Task NotifyItemTypeCreated(IHubContext<ItemTypeHub> hubContext, ItemTypeDefinition definition)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeCreated", definition);
    }

    /// <summary>
    /// Broadcast item type updated event (for server-side use).
    /// </summary>
    public static async Task NotifyItemTypeUpdated(IHubContext<ItemTypeHub> hubContext, ItemTypeDefinition definition)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeUpdated", definition);
    }

    /// <summary>
    /// Broadcast item type deleted event (for server-side use).
    /// </summary>
    public static async Task NotifyItemTypeDeleted(IHubContext<ItemTypeHub> hubContext, string itemTypeId)
    {
        await hubContext.Clients.Group("item-types").SendAsync("ItemTypeDeleted", itemTypeId);
    }

    #endregion
}
