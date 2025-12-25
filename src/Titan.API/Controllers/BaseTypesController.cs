using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains.Items;
using Titan.Abstractions.Models.Items;
using Titan.Abstractions.RateLimiting;

namespace Titan.API.Controllers;

/// <summary>
/// Base type management endpoints for admins.
/// </summary>
[ApiController]
[Route("api/admin/base-types")]
[Tags("Admin - Base Types")]
[Authorize(Policy = "AdminDashboard")]
[RateLimitPolicy("Admin")]
public class BaseTypesController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<BaseTypesController> _logger;
    private readonly IValidator<CreateBaseTypeRequest> _createValidator;
    private readonly IValidator<UpdateBaseTypeRequest> _updateValidator;

    public BaseTypesController(
        IClusterClient clusterClient,
        ILogger<BaseTypesController> logger,
        IValidator<CreateBaseTypeRequest> createValidator,
        IValidator<UpdateBaseTypeRequest> updateValidator)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    private IBaseTypeRegistryGrain GetGrain() => _clusterClient.GetGrain<IBaseTypeRegistryGrain>("default");

    /// <summary>
    /// Get all base types.
    /// </summary>
    /// <returns>List of all registered base types.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BaseType>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BaseType>>> GetAll()
    {
        var types = await GetGrain().GetAllAsync();
        return Ok(types);
    }

    /// <summary>
    /// Get base type by ID.
    /// </summary>
    /// <param name="id">The base type identifier.</param>
    /// <returns>The requested base type.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<BaseType>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseType>> GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid base type ID" });
        }

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
    /// <param name="request">Base type creation details.</param>
    /// <returns>The created base type.</returns>
    [HttpPost]
    [ProducesResponseType<BaseType>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseType>> Create([FromBody] CreateBaseTypeRequest request)
    {
        var validationResult = await _createValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

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
    /// <param name="id">The base type identifier.</param>
    /// <param name="request">Updated base type details.</param>
    /// <returns>The updated base type.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType<BaseType>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseType>> Update(string id, [FromBody] UpdateBaseTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid base type ID" });
        }

        var validationResult = await _updateValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

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
    /// <param name="id">The base type identifier to delete.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        {
            return BadRequest(new { error = "Invalid base type ID" });
        }

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
