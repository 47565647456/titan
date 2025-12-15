using Microsoft.AspNetCore.SignalR.Client;
using NBomber.Contracts;
using NBomber.CSharp;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using Titan.LoadTests.Infrastructure;

namespace Titan.LoadTests.Scenarios;

/// <summary>
/// Trading scenario - tests full trade flow between two virtual users.
/// Uses pooled connections to simulate realistic persistent client behavior.
/// </summary>
public static class TradingScenario
{
    public static ScenarioProps Create(string baseUrl, int copies, TimeSpan duration)
    {
        TitanClientPool? pool = null;
        
        return Scenario.Create("trading_flow", async context =>
        {
            var (user1, user2) = pool?.RentPair() ?? (null, null);
            if (user1 == null || user2 == null)
            {
                pool?.ReturnPair(user1, user2);
                return Response.Fail(message: "Not enough clients available in pool");
            }
            
            try
            {
                // Step 1: Create characters for both
                var createChars = await Step.Run("create_characters", context, async () =>
                {
                    try
                    {
                        var accountHub1 = await user1.GetAccountHubAsync();
                        var accountHub2 = await user2.GetAccountHubAsync();
                        
                        var suffix = Guid.NewGuid().ToString("N");
                        
                        var char1 = await accountHub1.InvokeAsync<CharacterSummary>(
                            "CreateCharacter", "standard", $"Trader1_{suffix}", CharacterRestrictions.None);
                        var char2 = await accountHub2.InvokeAsync<CharacterSummary>(
                            "CreateCharacter", "standard", $"Trader2_{suffix}", CharacterRestrictions.None);
                        
                        if (char1 != null && char2 != null)
                        {
                            context.Data["char1Id"] = char1.CharacterId;
                            context.Data["char2Id"] = char2.CharacterId;
                            return Response.Ok(sizeBytes: 256);
                        }
                        return Response.Fail();
                    }
                    catch (Exception ex)
                    {
                        return Response.Fail(message: ex.Message);
                    }
                });
                
                if (createChars.IsError)
                    return Response.Fail();
                
                // Step 2: Start trade
                var startTrade = await Step.Run("start_trade", context, async () =>
                {
                    try
                    {
                        var char1Id = (Guid)context.Data["char1Id"];
                        var char2Id = (Guid)context.Data["char2Id"];
                        
                        var tradeHub1 = await user1.GetTradeHubAsync();
                        var session = await tradeHub1.InvokeAsync<TradeSession>(
                            "StartTrade", char1Id, char2Id, "standard");
                        
                        if (session != null)
                        {
                            context.Data["tradeId"] = session.TradeId;
                            return Response.Ok(sizeBytes: 128);
                        }
                        return Response.Fail();
                    }
                    catch (Exception ex)
                    {
                        return Response.Fail(message: ex.Message);
                    }
                });
                
                if (startTrade.IsError)
                    return Response.Fail();
                
                // Step 3: Both accept trade (no items - empty trade)
                var completeTrade = await Step.Run("complete_trade", context, async () =>
                {
                    try
                    {
                        var tradeId = (Guid)context.Data["tradeId"];
                        
                        var tradeHub1 = await user1.GetTradeHubAsync();
                        var tradeHub2 = await user2.GetTradeHubAsync();
                        
                        // User 1 accepts
                        await tradeHub1.InvokeAsync<AcceptResult>("AcceptTrade", tradeId);
                        
                        // User 2 accepts - completes the trade
                        var result = await tradeHub2.InvokeAsync<AcceptResult>("AcceptTrade", tradeId);
                        
                        return result?.Completed == true
                            ? Response.Ok(sizeBytes: 128)
                            : Response.Fail();
                    }
                    catch (Exception ex)
                    {
                        return Response.Fail(message: ex.Message);
                    }
                });
                
                return completeTrade.IsError ? Response.Fail() : Response.Ok();
            }
            finally
            {
                pool?.ReturnPair(user1, user2);
            }
        })
        .WithInit(async context =>
        {
            // Need 2 clients per iteration, plus buffer for concurrent access
            pool = new TitanClientPool(baseUrl);
            await pool.InitializeAsync(copies * 4);
        })
        .WithClean(async context =>
        {
            if (pool != null)
                await pool.DisposeAsync();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.KeepConstant(copies: copies, during: duration)
        );
    }
}

internal record AcceptResult(TradeStatus Status, bool Completed);
