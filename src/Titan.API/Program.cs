using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Orleans.Configuration;
using Scalar.AspNetCore;
using System.Text;
using Titan.Abstractions;
using Titan.API.Data;
using Titan.API.Hubs;
using Titan.API.Services;
using Titan.API.Services.Auth;
using Titan.Abstractions.Rules;
using Titan.Grains.Trading.Rules;
using Titan.API.Config;
using Titan.API.Controllers;
using Titan.API.Services.RateLimiting;
using Titan.API.OpenApi;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// Register all FluentValidation validators from this assembly
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Register HubValidationService for SignalR hub validation
builder.Services.AddScoped<Titan.API.Services.HubValidationService>();


// Validate configuration early (fails fast if critical config is missing)
builder.ValidateTitanConfiguration(requireJwtKey: true, requireEosInProduction: true);

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// Add Redis client for Orleans clustering (keyed service registration)
// Key must match Redis resource name from AppHost's AddRedis()
builder.AddKeyedRedisClient("orleans-clustering");

// Add Redis client for rate limiting state
builder.AddKeyedRedisClient("rate-limiting");

// Configure EF Core with PostgreSQL for Admin Identity
var adminConnectionString = builder.Configuration.GetConnectionString("titan-admin");
if (!string.IsNullOrEmpty(adminConnectionString))
{
    builder.Services.AddDbContext<AdminDbContext>(options =>
        options.UseNpgsql(adminConnectionString));
    
    // Use AddIdentityCore instead of AddIdentity to avoid overriding the default JWT auth scheme
    // AddIdentity sets up cookie auth as default, which breaks our JWT-based SignalR hubs
    builder.Services.AddIdentityCore<AdminUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<AdminRole>()
    .AddEntityFrameworkStores<AdminDbContext>()
    .AddSignInManager()
    .AddRoleManager<RoleManager<AdminRole>>()
    .AddDefaultTokenProviders();
    
    // Register AccountQueryService for admin dashboard
    builder.Services.AddSingleton<AccountQueryService>();
}

// Configure Sentry SDK for ASP.NET Core (only if DSN is configured)
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrEmpty(sentryDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Configuration["Sentry:Environment"] 
            ?? builder.Environment.EnvironmentName;
        options.TracesSampleRate = builder.Configuration.GetValue("Sentry:TracesSampleRate", 0.1);
        options.Debug = builder.Configuration.GetValue("Sentry:Debug", false);
        options.SendDefaultPii = false;
    });
}

// Configure Serilog with file logging (dev) and Sentry sink (production)
builder.AddTitanLogging("api");

// Configure Orleans Client
// Clustering is auto-configured by Aspire via Redis
builder.UseOrleansClient(client =>
{
    // CRITICAL: Must match ServiceId used by silos for clustering to work
    client.Configure<ClusterOptions>(options => options.ServiceId = "titan-service");
    
    // Add stream support for receiving trade events
    client.AddMemoryStreams(TradeStreamConstants.ProviderName);
});

// Add SignalR for WebSocket hubs with rate limiting filter
builder.Services.AddSignalR(options =>
{
    // Enable detailed errors in development/testing for debugging
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors = true;
    }
}).AddHubOptions<Titan.API.Hubs.TitanHubBase>(options =>
{
    // Hub-specific options if needed
});

// Register hub filter for rate limiting (applied to all hubs)
builder.Services.AddSingleton<IHubFilter, RateLimitHubFilter>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Titan API",
            Version = "v1",
            Description = "API Gateway for the Titan game backend. Provides HTTP authentication endpoints and admin management APIs. Real-time game operations use SignalR hubs (see SignalR documentation).",
            Contact = new() { Name = "Titan Team" }
        };
        return Task.CompletedTask;
    });
});

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

// EOS Options (only validate in production or when EOS is configured)
var eosSection = builder.Configuration.GetSection(EosOptions.SectionName);
if (eosSection.Exists() && !string.IsNullOrEmpty(eosSection["ClientId"]))
{
    builder.Services.AddOptions<EosOptions>()
        .Bind(eosSection)
        .ValidateDataAnnotations()
        .ValidateOnStart();
}

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
        // Uses ticket-based authentication only (no JWT tokens in URLs to prevent log exposure)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = async context =>
            {
                var path = context.HttpContext.Request.Path;
                
                // Match all SignalR hub endpoints
                var hubPaths = new[] 
                { 
                    "/accountHub", "/authHub", "/characterHub", 
                    "/inventoryHub", "/baseTypeHub", "/seasonHub", "/tradeHub",
                    "/hubs/admin-metrics"
                };
                
                var isHubPath = hubPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
                if (!isHubPath)
                {
                    return;
                }

                // Ticket-based authentication (prevents JWT tokens from appearing in server logs)
                var ticket = context.Request.Query["ticket"].FirstOrDefault();
                if (string.IsNullOrEmpty(ticket))
                {
                    // No ticket provided - auth will fail
                    return;
                }

                try
                {
                    var clusterClient = context.HttpContext.RequestServices.GetRequiredService<IClusterClient>();
                    var ticketGrain = clusterClient.GetGrain<Titan.Abstractions.Grains.IConnectionTicketGrain>(ticket);
                    var validTicket = await ticketGrain.ValidateAndConsumeAsync();

                    if (validTicket != null)
                    {
                        // Create claims identity from ticket
                        var claims = new List<System.Security.Claims.Claim>
                        {
                            new(System.Security.Claims.ClaimTypes.NameIdentifier, validTicket.UserId.ToString())
                        };
                        claims.AddRange(validTicket.Roles.Select(r => 
                            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, r)));

                        context.Principal = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(claims, "Ticket"));
                        context.Success();
                        return;
                    }
                    else
                    {
                        // Ticket grain returned null - ticket not found, expired, or already consumed
                        var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                        logger?.LogWarning("Ticket validation returned null for ticket: {TicketPrefix}... (not found, expired, or consumed)", 
                            ticket.Length > 8 ? ticket[..8] : ticket);
                    }
                }
                catch (Exception ex)
                {
                    // Log ticket validation failure for diagnostics
                    var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
                    logger?.LogWarning(ex, "Ticket validation failed for ticket: {TicketPrefix}...", 
                        ticket.Length > 8 ? ticket[..8] : ticket);
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    // SuperAdmin policy - requires "admin" or "superadmin" role claim
    options.AddPolicy("SuperAdmin", policy => 
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => 
                c.Type == System.Security.Claims.ClaimTypes.Role && 
                (c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                 c.Value.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)))));
    
    // AdminDashboard policy - requires any admin role (SuperAdmin, Admin, or Viewer)
    options.AddPolicy("AdminDashboard", policy => 
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => 
                c.Type == System.Security.Claims.ClaimTypes.Role && 
                (c.Value.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                 c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                 c.Value.Equals("Viewer", StringComparison.OrdinalIgnoreCase)))));
});

// Add controllers for admin API endpoints
builder.Services.AddControllers();

// Register Auth Services
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddHttpClient<EosConnectService>();

// Register auth providers as keyed services
// EOS is always registered (will fail at runtime if options missing in production)
builder.Services.AddKeyedSingleton<IAuthService, EosConnectService>("EOS");

// Mock auth only in development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddKeyedSingleton<IAuthService, MockAuthService>("Mock");
}

// Auth service factory for provider selection
builder.Services.AddSingleton<IAuthServiceFactory, AuthServiceFactory>();

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

// Register rate limiting services (Redis-backed)
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddSingleton<RateLimitHubFilter>();
builder.Services.AddHostedService<RateLimitConfigInitializer>();

// Register admin metrics broadcaster for SignalR push updates
builder.Services.AddSingleton<AdminMetricsBroadcaster>();

var app = builder.Build();

// Seed default admin user if none exists
await SeedAdminUsersAsync(app);

// Map Aspire default health check endpoints
app.MapDefaultEndpoints();

async Task SeedAdminUsersAsync(WebApplication app)
{
    // Only seed if admin DB is configured
    if (string.IsNullOrEmpty(adminConnectionString)) return;
    
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AdminRole>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Ensure database is created
        await dbContext.Database.EnsureCreatedAsync();
        
        // Create default roles if they don't exist
        string[] roles = ["SuperAdmin", "Admin", "Viewer"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new AdminRole { Name = roleName });
                logger.LogInformation("Created role: {Role}", roleName);
            }
        }
        
        // Create default admin user if no users exist
        if (!await userManager.Users.AnyAsync())
        {
            // Get admin credentials from configuration, with fallbacks for development
            var adminEmail = config["Admin:DefaultEmail"] ?? "admin@titan.local";
            var adminPassword = config["Admin:DefaultPassword"] ?? "Admin123!";
            var adminDisplayName = config["Admin:DefaultDisplayName"] ?? "Default Admin";
            
            var adminUser = new AdminUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = adminDisplayName,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            
            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRolesAsync(adminUser, ["SuperAdmin", "Admin"]);
                logger.LogInformation("Created default admin user: {Id}", adminUser.Id);
            }
            else
            {
                logger.LogWarning("Failed to create default admin: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to seed admin users");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Titan API")
            .WithTheme(ScalarTheme.DeepSpace)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.UseCors();

// Rate limiting middleware - applies to all routes
// Policy matching is configured in appsettings.json:
// - /api/admin/auth/* -> "Auth" (strict)
// - /api/admin/* -> "Admin" (1000/min, defense-in-depth)
// - /hubs/admin* -> "AdminHub" (5000/min, real-time metrics)
// - Everything else -> "Global" (default)
app.UseMiddleware<RateLimitMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Map WebSocket Hubs (replacing HTTP controllers)
app.MapHub<AccountHub>("/accountHub");
app.MapHub<AuthHub>("/authHub");
app.MapHub<CharacterHub>("/characterHub");
app.MapHub<InventoryHub>("/inventoryHub");
app.MapHub<BaseTypeHub>("/baseTypeHub");
app.MapHub<SeasonHub>("/seasonHub");
app.MapHub<TradeHub>("/tradeHub");

// Admin dashboard SignalR hub for real-time metrics
app.MapHub<AdminMetricsHub>("/hubs/admin-metrics");

// Map HTTP Authentication API (industry standard: HTTP for auth, WebSocket for real-time)
app.MapAuthEndpoints();

// Map controllers for admin API
app.MapControllers();

app.Run();
