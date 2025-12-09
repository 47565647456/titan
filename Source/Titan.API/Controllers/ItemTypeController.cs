using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Hubs;

namespace Titan.API.Controllers;

/// <summary>
/// Admin API for managing item types at runtime.
/// Requires admin role for write operations.
/// </summary>
[ApiController]
[Route("api/item-types")]
public class ItemTypeController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly IHubContext<ItemTypeHub> _hubContext;
    private readonly ILogger<ItemTypeController> _logger;

    public ItemTypeController(
        IGrainFactory grainFactory,
        IHubContext<ItemTypeHub> hubContext,
        ILogger<ItemTypeController> logger)
    {
        _grainFactory = grainFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all registered item types.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ItemTypeDefinition>>> GetAll()
    {
        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var items = await registry.GetAllAsync();
        return Ok(items);
    }

    /// <summary>
    /// Get a specific item type by ID.
    /// </summary>
    [HttpGet("{itemTypeId}")]
    public async Task<ActionResult<ItemTypeDefinition>> Get(string itemTypeId)
    {
        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        var definition = await registry.GetAsync(itemTypeId);
        
        if (definition == null)
            return NotFound(new { error = $"Item type '{itemTypeId}' not found." });

        return Ok(definition);
    }

    /// <summary>
    /// Create a new item type.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ItemTypeDefinition>> Create([FromBody] ItemTypeDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ItemTypeId))
            return BadRequest(new { error = "ItemTypeId is required." });

        if (string.IsNullOrWhiteSpace(definition.Name))
            return BadRequest(new { error = "Name is required." });

        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");
        
        // Check if already exists
        if (await registry.ExistsAsync(definition.ItemTypeId))
            return Conflict(new { error = $"Item type '{definition.ItemTypeId}' already exists." });

        try
        {
            await registry.RegisterAsync(definition);
            _logger.LogInformation("Item type '{ItemTypeId}' created by {User}", definition.ItemTypeId, User?.Identity?.Name ?? "unknown");
            
            // Notify connected clients
            await ItemTypeHub.NotifyItemTypeCreated(_hubContext, definition);
            
            return CreatedAtAction(nameof(Get), new { itemTypeId = definition.ItemTypeId }, definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing item type.
    /// </summary>
    [HttpPut("{itemTypeId}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<ItemTypeDefinition>> Update(string itemTypeId, [FromBody] ItemTypeDefinition definition)
    {
        if (definition.ItemTypeId != itemTypeId)
            return BadRequest(new { error = "ItemTypeId in URL must match body." });

        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(itemTypeId))
            return NotFound(new { error = $"Item type '{itemTypeId}' not found." });

        try
        {
            await registry.UpdateAsync(definition);
            _logger.LogInformation("Item type '{ItemTypeId}' updated by {User}", itemTypeId, User?.Identity?.Name ?? "unknown");
            
            // Notify connected clients
            await ItemTypeHub.NotifyItemTypeUpdated(_hubContext, definition);
            
            return Ok(definition);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete an item type.
    /// </summary>
    [HttpDelete("{itemTypeId}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> Delete(string itemTypeId)
    {
        var registry = _grainFactory.GetGrain<IItemTypeRegistryGrain>("default");

        if (!await registry.ExistsAsync(itemTypeId))
            return NotFound(new { error = $"Item type '{itemTypeId}' not found." });

        await registry.DeleteAsync(itemTypeId);
        _logger.LogInformation("Item type '{ItemTypeId}' deleted by {User}", itemTypeId, User?.Identity?.Name ?? "unknown");
        
        // Notify connected clients
        await ItemTypeHub.NotifyItemTypeDeleted(_hubContext, itemTypeId);

        return NoContent();
    }
}
