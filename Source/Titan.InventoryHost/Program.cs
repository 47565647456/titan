using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Titan.Abstractions;

var builder = Host.CreateApplicationBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// Add Redis client for Orleans clustering (keyed service registration)
// Key must match Redis resource name from AppHost's AddRedis()
builder.AddKeyedRedisClient("orleans-clustering");

// Configure Serilog
builder.Services.AddSerilog(config => 
{
    config.WriteTo.Console();
    var logPath = builder.Configuration["Logging:FilePath"] ?? "logs/titan-inventory-.txt";
    config.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);
});

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
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams(TradeStreamConstants.ProviderName);

    // Grain persistence using PostgreSQL (connection string injected by Aspire)
    var connectionString = builder.Configuration.GetConnectionString("titan");
    if (!string.IsNullOrEmpty(connectionString))
    {
        silo.AddAdoNetGrainStorage("OrleansStorage", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });
        
        // Transaction store using ADO.NET
        silo.AddAdoNetGrainStorage("TransactionStore", options =>
        {
            options.Invariant = "Npgsql";
            options.ConnectionString = connectionString;
        });
    }
    else
    {
        // Fallback to memory storage for local dev without Aspire
        silo.AddMemoryGrainStorage("OrleansStorage");
        silo.AddMemoryGrainStorage("TransactionStore");
    }
});

var host = builder.Build();
host.Run();
