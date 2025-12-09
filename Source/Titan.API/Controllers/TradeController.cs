using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Hubs;

namespace Titan.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradeController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    private readonly IHubContext<TradeHub> _hubContext;

    public TradeController(IClusterClient clusterClient, IHubContext<TradeHub> hubContext)
    {
        _clusterClient = clusterClient;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Start a new trade session between two characters.
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> StartTrade([FromBody] StartTradeRequest request)
    {
        var tradeId = Guid.NewGuid();
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        
        try
        {
            var session = await grain.InitiateAsync(request.InitiatorCharacterId, request.TargetCharacterId, request.SeasonId);
            await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "TradeStarted", session);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Get trade session details.
    /// </summary>
    [HttpGet("{tradeId:guid}")]
    public async Task<IActionResult> GetTrade(Guid tradeId)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        try
        {
            var session = await grain.GetSessionAsync();
            return Ok(session);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Add an item to the trade.
    /// </summary>
    [HttpPost("{tradeId:guid}/items")]
    public async Task<IActionResult> AddItem(Guid tradeId, [FromBody] TradeItemRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.AddItemAsync(request.CharacterId, request.ItemId);
        var session = await grain.GetSessionAsync();

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "ItemAdded", new { request.CharacterId, request.ItemId });

        return Ok(session);
    }

    /// <summary>
    /// Remove an item from the trade.
    /// </summary>
    [HttpDelete("{tradeId:guid}/items")]
    public async Task<IActionResult> RemoveItem(Guid tradeId, [FromBody] TradeItemRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.RemoveItemAsync(request.CharacterId, request.ItemId);
        var session = await grain.GetSessionAsync();

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "ItemRemoved", new { request.CharacterId, request.ItemId });

        return Ok(session);
    }

    /// <summary>
    /// Accept the trade offer.
    /// </summary>
    [HttpPost("{tradeId:guid}/accept")]
    public async Task<IActionResult> AcceptTrade(Guid tradeId, [FromBody] AcceptTradeRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        var status = await grain.AcceptAsync(request.CharacterId);

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, status == TradeStatus.Completed ? "TradeCompleted" : "TradeAccepted", new { request.CharacterId, Status = status.ToString() });

        return Ok(new { Status = status.ToString() });
    }

    /// <summary>
    /// Cancel the trade.
    /// </summary>
    [HttpPost("{tradeId:guid}/cancel")]
    public async Task<IActionResult> CancelTrade(Guid tradeId, [FromBody] CancelTradeRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.CancelAsync(request.CharacterId);

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "TradeCancelled", new { request.CharacterId });

        return Ok(new { Status = "Cancelled" });
    }
}

public record StartTradeRequest(Guid InitiatorCharacterId, Guid TargetCharacterId, string SeasonId);
public record TradeItemRequest(Guid CharacterId, Guid ItemId);
public record AcceptTradeRequest(Guid CharacterId);
public record CancelTradeRequest(Guid CharacterId);
