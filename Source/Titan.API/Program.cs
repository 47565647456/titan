using Microsoft.Extensions.Hosting;
using Serilog;
using Titan.API.Hubs;
using Titan.API.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, config) => 
{
    config.WriteTo.Console();
    config.WriteTo.File("logs/titan-api-.txt", rollingInterval: RollingInterval.Day);
});

// Configure Orleans Client
builder.Host.UseOrleansClient(client =>
{
    // Connect to the Localhost Cluster via Gateways (Identity, Inventory, Trading)
    client.UseLocalhostClustering(new[] { 30001, 30002, 30003 });
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Auth Services
builder.Services.AddSingleton<IAuthService, MockAuthService>(); // Use Mock by default for dev
builder.Services.AddHttpClient(); // For EOS auth if needed

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TradeHub>("/tradeHub");

app.Run();

