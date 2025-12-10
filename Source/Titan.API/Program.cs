using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// Add Redis client for Orleans clustering (keyed service registration)
// Key must match Redis resource name from AppHost's AddRedis()
builder.AddKeyedRedisClient("orleans-clustering");

// Configure Serilog
builder.Host.UseSerilog((context, config) => 
{
    config.WriteTo.Console();
    config.WriteTo.File("logs/titan-api-.txt", rollingInterval: RollingInterval.Day);
});

// Configure Orleans Client
// Clustering is auto-configured by Aspire via Redis
builder.UseOrleansClient(client =>
{
    // Add stream support for receiving trade events
    client.AddMemoryStreams(TradeStreamConstants.ProviderName);
});

// Add SignalR for WebSocket hubs
builder.Services.AddSignalR();
builder.Services.AddOpenApi();

// JWT Authentication for secured hub methods
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
        
        // Configure JWT for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                // If the request is for a hub, extract the token from query string
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
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

// Map Aspire default health check endpoints
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Map WebSocket Hubs (replacing HTTP controllers)
app.MapHub<AccountHub>("/accountHub");
app.MapHub<AuthHub>("/authHub");
app.MapHub<CharacterHub>("/characterHub");
app.MapHub<InventoryHub>("/inventoryHub");
app.MapHub<ItemTypeHub>("/itemTypeHub");
app.MapHub<SeasonHub>("/seasonHub");
app.MapHub<TradeHub>("/tradeHub");

app.Run();
