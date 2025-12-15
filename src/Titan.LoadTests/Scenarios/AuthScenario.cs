using NBomber.Contracts;
using NBomber.CSharp;
using Titan.LoadTests.Infrastructure;

namespace Titan.LoadTests.Scenarios;

/// <summary>
/// Authentication throughput scenario - tests HTTP login endpoint.
/// This scenario creates NEW connections to measure login throughput.
/// </summary>
public static class AuthScenario
{
    public static ScenarioProps Create(string baseUrl, int rate, TimeSpan duration)
    {
        return Scenario.Create("auth_login", async context =>
        {
            // Auth scenario intentionally creates new connections each time
            // to measure login endpoint throughput (not connection reuse)
            await using var client = new TitanClient(baseUrl);
            
            var success = await client.LoginAsync();
            
            return success 
                ? Response.Ok(sizeBytes: 256)
                : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: rate, interval: TimeSpan.FromSeconds(1), during: duration)
        );
    }
}
