using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Titan.Dashboard.Components;
using Titan.Dashboard.Data;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire ServiceDefaults (OpenTelemetry, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// Add Redis client for Orleans clustering (keyed service registration)
builder.AddKeyedRedisClient("orleans-clustering");

// Configure Serilog with file logging (dev) and Sentry sink (production)
builder.AddTitanLogging("dashboard");

// Configure Orleans Client (connect to the existing cluster)
builder.UseOrleansClient();

// Register AccountQueryService for direct database queries (account listing)
builder.Services.AddSingleton<Titan.Dashboard.Services.AccountQueryService>();

// Configure EF Core with CockroachDB/PostgreSQL for Identity (separate database)
var connectionString = builder.Configuration.GetConnectionString("titan-admin") 
    ?? throw new InvalidOperationException("Connection string 'titan-admin' not found. Ensure the Dashboard is started via Aspire.");
builder.Services.AddDbContext<AdminDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure ASP.NET Core Identity
builder.Services.AddIdentity<AdminUser, AdminRole>(options =>
{
    // Password requirements
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AdminDbContext>()
.AddDefaultTokenProviders();

// Configure cookie authentication for Blazor Server
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

// Add authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdmin", policy => policy.RequireRole("SuperAdmin"))
    .AddPolicy("Admin", policy => policy.RequireRole("SuperAdmin", "Admin"))
    .AddPolicy("Viewer", policy => policy.RequireRole("SuperAdmin", "Admin", "Viewer"));

// Add Blazor components with cascading authentication state for interactive mode
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Map Aspire default health check endpoints
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Seed default admin roles and user on startup (development only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await SeedAdminDataAsync(services);
}

app.Run();

/// <summary>
/// Seeds default admin roles and a SuperAdmin user for development.
/// </summary>
static async Task SeedAdminDataAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<AdminRole>>();
    var userManager = services.GetRequiredService<UserManager<AdminUser>>();
    var configuration = services.GetRequiredService<IConfiguration>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    // Seed roles
    var roles = new[] { "SuperAdmin", "Admin", "Viewer" };
    foreach (var roleName in roles)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = new AdminRole(roleName)
            {
                Description = roleName switch
                {
                    "SuperAdmin" => "Full access including admin management",
                    "Admin" => "Game management access",
                    "Viewer" => "Read-only access",
                    _ => null
                }
            };
            await roleManager.CreateAsync(role);
            logger.LogInformation("Created role: {RoleName}", roleName);
        }
    }

    // Seed default SuperAdmin user from configuration
    var seedEmail = configuration["AdminSeed:Email"];
    var seedPassword = configuration["AdminSeed:Password"];
    var seedDisplayName = configuration["AdminSeed:DisplayName"] ?? "Default Admin";
    
    if (string.IsNullOrEmpty(seedEmail) || string.IsNullOrEmpty(seedPassword))
    {
        logger.LogWarning("AdminSeed configuration not found, skipping default admin seeding");
        return;
    }
    
    var existingUser = await userManager.FindByEmailAsync(seedEmail);
    if (existingUser == null)
    {
        var adminUser = new AdminUser
        {
            UserName = seedEmail,
            Email = seedEmail,
            EmailConfirmed = true,
            DisplayName = seedDisplayName
        };

        var result = await userManager.CreateAsync(adminUser, seedPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
            logger.LogInformation("Created default SuperAdmin user: {Email}", seedEmail);
        }
        else
        {
            logger.LogWarning("Failed to create default admin: {Errors}", 
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}
