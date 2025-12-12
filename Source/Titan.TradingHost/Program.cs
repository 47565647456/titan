using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Titan.Abstractions;

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

// Configure Orleans Silo
// Clustering is auto-configured by Aspire via Redis
builder.UseOrleans(silo =>
{
    // Enable Orleans transactions for atomic multi-grain operations
    silo.UseTransactions();

    // Memory Streams for trade events (cross-silo pub/sub)
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams(TradeStreamConstants.ProviderName);

    // Grain persistence - auto-configured based on Database:Type
    silo.AddTitanGrainStorage(builder.Configuration);
});

var host = builder.Build();
host.Run();
