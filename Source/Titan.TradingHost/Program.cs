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
    config.WriteTo.File("logs/titan-trading-.txt", rollingInterval: RollingInterval.Day);
});

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
