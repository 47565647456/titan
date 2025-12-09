using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Text;
using Titan.Abstractions;
using Titan.API.Hubs;
using Titan.API.Services;
using Titan.API.Services.Auth;
using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;

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
    
    // Add stream support for receiving trade events
    client.AddMemoryStreams(TradeStreamConstants.ProviderName);
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

// JWT Authentication for Admin API
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DevelopmentSecretKeyThatIsAtLeast32BytesLong!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// Register Auth Services
builder.Services.AddSingleton<IAuthService, MockAuthService>(); // Use Mock by default for dev
builder.Services.AddHttpClient(); // For EOS auth if needed

// Register Trade Stream Subscriber (singleton so it's shared across hubs)
builder.Services.AddSingleton<TradeStreamSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TradeStreamSubscriber>());

// Register Trade Rules
builder.Services.AddSingleton<IRule<TradeRequestContext>, SameSeasonRule>();
builder.Services.AddSingleton<IRule<TradeRequestContext>, SoloSelfFoundRule>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TradeHub>("/tradeHub");
app.MapHub<ItemTypeHub>("/itemTypeHub");
app.MapHub<SeasonHub>("/seasonHub");

app.Run();
