using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Titan.Abstractions;
using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;

namespace Titan.Tests;

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        var useDatabase = Environment.GetEnvironmentVariable("USE_DATABASE") == "true";
        
        // Configure Trading Options for tests (5-second timeout for expiration tests)
        siloBuilder.Services.Configure<TradingOptions>(options =>
        {
            options.TradeTimeout = TimeSpan.FromSeconds(5);
            options.ExpirationCheckInterval = TimeSpan.FromSeconds(1);
        });

        // Configure Item Registry Options for tests
        siloBuilder.Services.Configure<ItemRegistryOptions>(options =>
        {
            options.AllowUnknownItemTypes = true; // Allow unknown types in tests
        });

        // Memory Streams for trade events (requires PubSubStore)
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
        siloBuilder.AddMemoryStreams(TradeStreamConstants.ProviderName);

        // Use environment variable to determine storage type
        // CI sets USE_DATABASE=true, local dev defaults to memory
        if (useDatabase)
        {
            // Use real YugabyteDB for integration tests
            siloBuilder.AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = Environment.GetEnvironmentVariable("YUGABYTE_CONNECTION") 
                    ?? "Host=localhost;Port=5433;Database=titan;Username=yugabyte;Password=yugabyte";
            });
        }
        else
        {
            // Use in-memory storage for fast local development
            siloBuilder.AddMemoryGrainStorage("OrleansStorage");
        }

        // Register Trade Rules for Testing
        siloBuilder.Services.AddSingleton<IRule<TradeRequestContext>, SameSeasonRule>();
        siloBuilder.Services.AddSingleton<IRule<TradeRequestContext>, SoloSelfFoundRule>();
    }
}

/// <summary>
/// Configures the test cluster client with stream provider access.
/// Required for client-side stream subscriptions in tests.
/// </summary>
public class TestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        // Add stream provider to client so tests can subscribe
        clientBuilder.AddMemoryStreams(TradeStreamConstants.ProviderName);
    }
}
