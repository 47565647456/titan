using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Serialization;
using Titan.Abstractions;
using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;
using Titan.ServiceDefaults.Serialization;

var builder = Host.CreateApplicationBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// Add Redis client for Orleans clustering (keyed service registration)
// Key must match Redis resource name from AppHost's AddRedis()
builder.AddKeyedRedisClient("orleans-clustering");

// Configure Serilog with file logging (dev) and Sentry sink (production)
builder.AddTitanLogging("trading-host");

// Configure Trading Options
builder.Services.Configure<TradingOptions>(options =>
{
    // Defaults: 15 min timeout, 1 min check interval
    // Can be overridden via appsettings.json "Trading" section
});

// Bind from configuration if available
var tradingSection = builder.Configuration.GetSection(TradingOptions.SectionName);
if (tradingSection.Exists())
{
    builder.Services.Configure<TradingOptions>(tradingSection);
}

// Configure Item History
builder.Services.Configure<ItemHistoryOptions>(options =>
{
    // Defaults: 100 entries per item, 90 day retention
});
var historySection = builder.Configuration.GetSection(ItemHistoryOptions.SectionName);
if (historySection.Exists())
{
    builder.Services.Configure<ItemHistoryOptions>(historySection);
}

// Configure Item Registry Cache (for BaseTypeReaderGrain, ModifierReaderGrain)
builder.Services.Configure<ItemRegistryCacheOptions>(options =>
{
    // Default: 5 minute cache (set by class default)
});

// Register trade validation rules
builder.Services.AddSingleton<IRule<TradeRequestContext>, SameSeasonRule>();
builder.Services.AddSingleton<IRule<TradeRequestContext>, SoloSelfFoundRule>();

// Configure Orleans Silo
// Clustering is auto-configured by Aspire via Redis
builder.UseOrleans(silo =>
{
    // CRITICAL: Set a stable ServiceId for grain state persistence across restarts
    // Without this, Aspire generates a random ServiceId each time and grain state is "lost"
    silo.Configure<ClusterOptions>(options => options.ServiceId = "titan-service");
    
    // Enable Orleans transactions for atomic multi-grain operations
    silo.UseTransactions();

    // Memory Streams for trade events (cross-silo pub/sub)
    // PubSubStore is now configured in AddTitanGrainStorage (persistent)
    silo.AddMemoryStreams(TradeStreamConstants.ProviderName);

    // Grain persistence - auto-configured based on Database:Type
    silo.AddTitanGrainStorage(builder.Configuration);
});

// Register MemoryPack serializer for Orleans wire serialization
builder.Services.AddSerializer(sb => sb.AddMemoryPackSerializer());

var host = builder.Build();
host.Run();
