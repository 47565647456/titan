using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Titan.Abstractions;

var builder = Host.CreateDefaultBuilder(args);

builder.UseSerilog((context, config) => 
{
    config.WriteTo.Console();
    config.WriteTo.File("logs/titan-trading-.txt", rollingInterval: RollingInterval.Day);
});

// Configure Trading Options
builder.ConfigureServices((context, services) =>
{
    services.Configure<TradingOptions>(options =>
    {
        // Defaults: 15 min timeout, 1 min check interval
        // Can be overridden via appsettings.json "Trading" section
    });
    
    // Bind from configuration if available
    var tradingSection = context.Configuration.GetSection(TradingOptions.SectionName);
    if (tradingSection.Exists())
    {
        services.Configure<TradingOptions>(tradingSection);
    }
});

builder.UseOrleans(silo =>
{
    // TradingHost connects to the cluster via Primary (IdentityHost)
    silo.UseLocalhostClustering(
        siloPort: 11113, 
        gatewayPort: 30003,
        primarySiloEndpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11111));

    silo.ConfigureLogging(logging => logging.AddConsole());

    // Memory Streams for trade events (cross-silo pub/sub)
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams(TradeStreamConstants.ProviderName);

    silo.AddAdoNetGrainStorage("OrleansStorage", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "Host=localhost;Port=26257;Database=titan;Username=root;SSL Mode=Disable";
    });
});

var host = builder.Build();
host.Run();
