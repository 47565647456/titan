using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Titan.Abstractions;
using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;
using Titan.ServiceDefaults.Serialization;

namespace Titan.Tests;

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        var useDatabase = Environment.GetEnvironmentVariable("USE_DATABASE") == "true";
        
        // Enable Orleans transactions for atomic multi-grain operations
        siloBuilder.UseTransactions();

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
            var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION") 
                ?? "Host=localhost;Port=5432;Database=titan;Username=postgres;Password=TitanDevelopmentPassword123!";
            
            // Use real PostgreSQL for integration tests with MemoryPack serialization
            siloBuilder.AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = connectionString;
                options.GrainStorageSerializer = MemoryPackSerializerExtensions.CreateMemoryPackGrainStorageSerializer();
            });
            siloBuilder.AddAdoNetGrainStorage("TransactionStore", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = connectionString;
                // TransactionStore uses System.Text.Json - Orleans transaction internals may not be MemoryPackable
                options.GrainStorageSerializer = MemoryPackSerializerExtensions.CreateSystemTextJsonGrainStorageSerializer();
            });
            siloBuilder.AddAdoNetGrainStorage("GlobalStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = connectionString;
                options.GrainStorageSerializer = MemoryPackSerializerExtensions.CreateMemoryPackGrainStorageSerializer();
            });
        }
        else
        {
            // Use in-memory storage for fast local development
            siloBuilder.AddMemoryGrainStorage("OrleansStorage");
            siloBuilder.AddMemoryGrainStorage("TransactionStore");
            siloBuilder.AddMemoryGrainStorage("GlobalStorage");
        }

        // Register Trade Rules for Testing
        siloBuilder.Services.AddSingleton<IRule<TradeRequestContext>, SameSeasonRule>();
        siloBuilder.Services.AddSingleton<IRule<TradeRequestContext>, SoloSelfFoundRule>();

        // Configure Item History Options for tests
        siloBuilder.Services.Configure<ItemHistoryOptions>(options =>
        {
            options.MaxEntriesPerItem = 50;  // Smaller limit for tests
            options.RetentionDays = 30;
        });

        // Disable caching for reader grains in tests (ensures fresh data)
        siloBuilder.Services.Configure<ItemRegistryCacheOptions>(options =>
        {
            options.CacheDuration = TimeSpan.Zero;  // Always refresh cache
        });
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
