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
builder.AddTitanLogging("inventory-host");

// Configure Item Registry Options
builder.Services.Configure<ItemRegistryOptions>(options =>
{
    // Default: allow unknown item types for development
    options.AllowUnknownItemTypes = true;
});

// Bind from configuration if available
var registrySection = builder.Configuration.GetSection(ItemRegistryOptions.SectionName);
if (registrySection.Exists())
{
    builder.Services.Configure<ItemRegistryOptions>(registrySection);
}

// Configure Orleans Silo
// Clustering is auto-configured by Aspire via Redis
builder.UseOrleans(silo =>
{
    // Enable Orleans transactions for atomic multi-grain operations
    silo.UseTransactions();

    // Memory Streams for trade events (cross-silo pub/sub)
    // PubSubStore is now configured in AddTitanGrainStorage (persistent)
    silo.AddMemoryStreams(TradeStreamConstants.ProviderName);

    // Grain persistence - auto-configured based on Database:Type
    silo.AddTitanGrainStorage(builder.Configuration);
});

var host = builder.Build();
host.Run();
