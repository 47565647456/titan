using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateDefaultBuilder(args);

builder.UseSerilog((context, config) => 
{
    config.WriteTo.Console();
    config.WriteTo.File("logs/titan-trading-.txt", rollingInterval: RollingInterval.Day);
});

builder.UseOrleans(silo =>
{
    // TradingHost connects to the cluster via Primary (IdentityHost)
    silo.UseLocalhostClustering(
        siloPort: 11113, 
        gatewayPort: 30003,
        primarySiloEndpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11111));

    silo.ConfigureLogging(logging => logging.AddConsole());

    silo.AddAdoNetGrainStorage("OrleansStorage", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "Host=localhost;Port=26257;Database=titan;Username=root;SSL Mode=Disable";
    });
});

var host = builder.Build();
host.Run();
