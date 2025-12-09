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
    /// Start a new trade session.
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> StartTrade([FromBody] StartTradeRequest request)
    {
        var tradeId = Guid.NewGuid();
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        var session = await grain.InitiateAsync(request.InitiatorUserId, request.TargetUserId);

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "TradeStarted", session);

        return Ok(session);
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
        await grain.AddItemAsync(request.UserId, request.ItemId);
        var session = await grain.GetSessionAsync();

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "ItemAdded", new { request.UserId, request.ItemId });

        return Ok(session);
    }

    /// <summary>
    /// Remove an item from the trade.
    /// </summary>
    [HttpDelete("{tradeId:guid}/items")]
    public async Task<IActionResult> RemoveItem(Guid tradeId, [FromBody] TradeItemRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.RemoveItemAsync(request.UserId, request.ItemId);
        var session = await grain.GetSessionAsync();

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "ItemRemoved", new { request.UserId, request.ItemId });

        return Ok(session);
    }

    /// <summary>
    /// Accept the trade offer.
    /// </summary>
    [HttpPost("{tradeId:guid}/accept")]
    public async Task<IActionResult> AcceptTrade(Guid tradeId, [FromBody] AcceptTradeRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        var status = await grain.AcceptAsync(request.UserId);

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, status == TradeStatus.Completed ? "TradeCompleted" : "TradeAccepted", new { request.UserId, Status = status.ToString() });

        return Ok(new { Status = status.ToString() });
    }

    /// <summary>
    /// Cancel the trade.
    /// </summary>
    [HttpPost("{tradeId:guid}/cancel")]
    public async Task<IActionResult> CancelTrade(Guid tradeId, [FromBody] CancelTradeRequest request)
    {
        var grain = _clusterClient.GetGrain<ITradeGrain>(tradeId);
        await grain.CancelAsync(request.UserId);

        await TradeHub.NotifyTradeUpdate(_hubContext, tradeId, "TradeCancelled", new { request.UserId });

        return Ok(new { Status = "Cancelled" });
    }
}

public record StartTradeRequest(Guid InitiatorUserId, Guid TargetUserId);
public record TradeItemRequest(Guid UserId, Guid ItemId);
public record AcceptTradeRequest(Guid UserId);
public record CancelTradeRequest(Guid UserId);
