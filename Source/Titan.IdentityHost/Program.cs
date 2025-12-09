using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateDefaultBuilder(args);

builder.UseSerilog((context, config) => 
{
    config.WriteTo.Console();
    config.WriteTo.File("logs/titan-identity-.txt", rollingInterval: RollingInterval.Day);
});

builder.UseOrleans(silo =>
{
    // IdentityHost is the Primary for Local Development
    silo.UseLocalhostClustering(
        siloPort: 11111, 
        gatewayPort: 30001,
        primarySiloEndpoint: new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 11111));

    silo.ConfigureLogging(logging => logging.AddConsole());
    
    // Dashboard for monitoring
    silo.UseDashboard(options => {
        options.Port = 8081;
    });

    silo.AddAdoNetGrainStorage("OrleansStorage", options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = "Host=localhost;Port=26257;Database=titan;Username=root;SSL Mode=Disable";
    });
});

var host = builder.Build();
host.Run();
