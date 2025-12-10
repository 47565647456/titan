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
using Titan.API.Config;
using Microsoft.Extensions.Options;

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
    var logPath = context.Configuration["Logging:FilePath"] ?? "logs/titan-api-.txt";
    config.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);
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

// Register and Bind Options
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// JWT Authentication for secured hub methods
// Fail fast in production if key not configured
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
if (jwtOptions == null || string.IsNullOrEmpty(jwtOptions.Key))
{
    throw new InvalidOperationException("Jwt:Key must be configured.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
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
        var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        
        policy.WithOrigins(corsOptions.AllowedOrigins)
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
            factory: _ => 
            {
                var rateLimitOptions = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitOptions.WindowMinutes)
                };
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
