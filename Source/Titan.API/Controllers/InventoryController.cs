using Microsoft.AspNetCore.Mvc;
using Titan.Abstractions.Grains;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IClusterClient _clusterClient;

    public InventoryController(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }

    /// <summary>
    /// Get all items for a user.
    /// </summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetInventory(Guid userId)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(userId);
        var items = await grain.GetItemsAsync();
        return Ok(items);
    }

    /// <summary>
    /// Add a new item to a user's inventory.
    /// </summary>
    [HttpPost("{userId:guid}/items")]
    public async Task<IActionResult> AddItem(Guid userId, [FromBody] AddItemRequest request)
    {
        var grain = _clusterClient.GetGrain<IInventoryGrain>(userId);
        var item = await grain.AddItemAsync(request.ItemTypeId, request.Quantity, request.Metadata);
        return Ok(item);
    }

    /// <summary>
    /// Get item history.
    /// </summary>
    [HttpGet("history/{itemId:guid}")]
    public async Task<IActionResult> GetItemHistory(Guid itemId)
    {
        var grain = _clusterClient.GetGrain<IItemHistoryGrain>(itemId);
        var history = await grain.GetHistoryAsync();
        return Ok(history);
    }
}

public record AddItemRequest(string ItemTypeId, int Quantity = 1, Dictionary<string, object>? Metadata = null);
