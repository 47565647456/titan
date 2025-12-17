using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;

namespace Titan.API.Controllers;

/// <summary>
/// Base type management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/base-types")]
[Tags("Admin - Base Types")]
[Authorize(Policy = "AdminDashboard")]
public class BaseTypesController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<BaseTypesController> _logger;

    public BaseTypesController(
        IClusterClient clusterClient,
        ILogger<BaseTypesController> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    private IBaseTypeRegistryGrain GetGrain() => _clusterClient.GetGrain<IBaseTypeRegistryGrain>("default");

    /// <summary>
    /// Get all base types.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BaseType>>> GetAll()
    {
        var types = await GetGrain().GetAllAsync();
        return Ok(types);
    }

    /// <summary>
    /// Get base type by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<BaseType>> GetById(string id)
    {
        var type = await GetGrain().GetAsync(id);
        if (type == null)
        {
            return NotFound();
        }
        return Ok(type);
    }

    /// <summary>
    /// Create a new base type.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BaseType>> Create([FromBody] CreateBaseTypeRequest request)
    {
        var baseType = new BaseType
        {
            BaseTypeId = request.BaseTypeId,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Slot = request.Slot,
            Width = request.Width,
            Height = request.Height,
            MaxStackSize = request.MaxStackSize,
            IsTradeable = request.IsTradeable
        };

        await GetGrain().RegisterAsync(baseType);
        _logger.LogInformation("Created base type {BaseTypeId}", baseType.BaseTypeId);

        return CreatedAtAction(nameof(GetById), new { id = baseType.BaseTypeId }, baseType);
    }

    /// <summary>
    /// Update a base type.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<BaseType>> Update(string id, [FromBody] UpdateBaseTypeRequest request)
    {
        var baseType = new BaseType
        {
            BaseTypeId = id,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Slot = request.Slot,
            Width = request.Width,
            Height = request.Height,
            MaxStackSize = request.MaxStackSize,
            IsTradeable = request.IsTradeable
        };

        await GetGrain().UpdateAsync(baseType);
        _logger.LogInformation("Updated base type {BaseTypeId}", id);

        return Ok(baseType);
    }

    /// <summary>
    /// Delete a base type.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await GetGrain().DeleteAsync(id);
        _logger.LogInformation("Deleted base type {BaseTypeId}", id);
        return NoContent();
    }
}

// DTOs

public record CreateBaseTypeRequest
{
    public required string BaseTypeId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ItemCategory Category { get; init; } = ItemCategory.Equipment;
    public EquipmentSlot Slot { get; init; } = EquipmentSlot.None;
    public int Width { get; init; } = 1;
    public int Height { get; init; } = 1;
    public int MaxStackSize { get; init; } = 1;
    public bool IsTradeable { get; init; } = true;
}

public record UpdateBaseTypeRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ItemCategory Category { get; init; }
    public EquipmentSlot Slot { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int MaxStackSize { get; init; }
    public bool IsTradeable { get; init; }
}
