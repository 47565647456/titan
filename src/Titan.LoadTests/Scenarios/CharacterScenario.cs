using Microsoft.AspNetCore.SignalR.Client;
using NBomber.Contracts;
using NBomber.CSharp;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using Titan.LoadTests.Infrastructure;

namespace Titan.LoadTests.Scenarios;

/// <summary>
/// Character operations scenario - tests character creation and inventory reads.
/// Uses pooled connections to simulate realistic persistent client behavior.
/// </summary>
public static class CharacterScenario
{
    public static ScenarioProps Create(string baseUrl, int copies, TimeSpan duration)
    {
        TitanClientPool? pool = null;
        
        return Scenario.Create("character_ops", async context =>
        {
            var client = pool?.Rent();
            if (client == null)
                return Response.Fail(message: "No clients available in pool");
            
            try
            {
                // Step 1: Create character
                var createChar = await Step.Run("create_character", context, async () =>
                {
                    try
                    {
                        var accountHub = await client.GetAccountHubAsync();
                        var charName = $"LoadTest_{Guid.NewGuid():N}";
                        
                        var character = await accountHub.InvokeAsync<CharacterSummary>(
                            "CreateCharacter", "standard", charName, CharacterRestrictions.None);
                        
                        if (character != null)
                        {
                            context.Data["characterId"] = character.CharacterId;
                            return Response.Ok(sizeBytes: 128);
                        }
                        return Response.Fail();
                    }
                    catch (Exception ex)
                    {
                        return Response.Fail(message: ex.Message);
                    }
                });
                
                if (createChar.IsError)
                    return Response.Fail();
                
                // Step 2: Read inventory stats
                var getStats = await Step.Run("get_stats", context, async () =>
                {
                    try
                    {
                        var characterId = (Guid)context.Data["characterId"];
                        var inventoryHub = await client.GetInventoryHubAsync();
                        var stats = await inventoryHub.InvokeAsync<CharacterStats>(
                            "GetStats", characterId, "standard");
                        
                        return stats != null 
                            ? Response.Ok(sizeBytes: 256)
                            : Response.Fail();
                    }
                    catch (Exception ex)
                    {
                        return Response.Fail(message: ex.Message);
                    }
                });
                
                return getStats.IsError ? Response.Fail() : Response.Ok();
            }
            finally
            {
                pool?.Return(client);
            }
        })
        .WithInit(async context =>
        {
            pool = new TitanClientPool(baseUrl);
            await pool.InitializeAsync(copies * 2); // Extra clients for availability
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
