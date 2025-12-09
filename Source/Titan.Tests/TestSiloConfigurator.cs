using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Titan.Abstractions;

namespace Titan.Tests;

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        // Configure Trading Options for tests (shorter timeout for faster testing)
        siloBuilder.Services.Configure<TradingOptions>(options =>
        {
            options.TradeTimeout = TimeSpan.FromSeconds(5);
            options.ExpirationCheckInterval = TimeSpan.FromSeconds(1);
        });

        // Memory Streams for trade events (requires PubSubStore)
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryStreams(TradeStreamConstants.ProviderName);

        // Use environment variable to determine storage type
        // CI sets USE_DATABASE=true, local dev defaults to memory
        var useDatabase = Environment.GetEnvironmentVariable("USE_DATABASE") == "true";
        
        if (useDatabase)
        {
            // Use real CockroachDB for integration tests
            siloBuilder.AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = Environment.GetEnvironmentVariable("COCKROACH_CONNECTION") 
                    ?? "Host=localhost;Port=26257;Database=titan;Username=root;SSL Mode=Disable";
            });
        }
        else
        {
            // Use in-memory storage for fast local development
            siloBuilder.AddMemoryGrainStorage("OrleansStorage");
        }
    }
}
