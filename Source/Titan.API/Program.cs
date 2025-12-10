using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Text;
using System.Threading.RateLimiting;
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
builder.Services.AddSignalR(options =>
{
    // Enable detailed errors in development/testing for debugging
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors = true;
    }
});
builder.Services.AddOpenApi();

// JWT Authentication for secured hub methods
// Fail fast in production if key not configured
if (!builder.Environment.IsDevelopment() && builder.Configuration["Jwt:Key"] == null)
{
    throw new InvalidOperationException("Jwt:Key must be configured in production. Set via environment variable or secrets.");
}

var jwtKey = builder.Configuration["Jwt:Key"] ?? "DevelopmentSecretKeyThatIsAtLeast32BytesLong!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Titan";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        
        // Configure JWT for SignalR WebSocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                // Match all SignalR hub endpoints
                var hubPaths = new[] 
                { 
                    "/accountHub", "/authHub", "/characterHub", 
                    "/inventoryHub", "/itemTypeHub", "/seasonHub", "/tradeHub" 
                };
                
                if (!string.IsNullOrEmpty(accessToken) && 
                    hubPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Register Auth Services
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IAuthService, MockAuthService>(); // Use Mock by default for dev
builder.Services.AddHttpClient(); // For EOS auth if needed

// Register Trade Stream Subscriber (singleton so it's shared across hubs)
builder.Services.AddSingleton<TradeStreamSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TradeStreamSubscriber>());

// Register Trade Rules
builder.Services.AddSingleton<IRule<TradeRequestContext>, SameSeasonRule>();
builder.Services.AddSingleton<IRule<TradeRequestContext>, SoloSelfFoundRule>();

// CORS configuration for SignalR WebSocket connections
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
            ?? (builder.Environment.IsDevelopment() 
                ? new[] { "https://localhost:5001", "http://localhost:5000" }
                : Array.Empty<string>());
        
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials(); // Required for SignalR
    });
});

// Rate limiting to prevent abuse
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
                Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:WindowMinutes", 1))
            }));
});

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
app.UseCors();
app.UseRateLimiter();
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
