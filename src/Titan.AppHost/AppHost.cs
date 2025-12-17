using Microsoft.Extensions.Configuration;
using Titan.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var env = builder.Environment.EnvironmentName.ToLowerInvariant();

// =============================================================================
// Container Image Configuration
// =============================================================================

// Read container image versions from configuration (defaults to pinned stable versions)
var redisImage = builder.Configuration["ContainerImages:Redis:Image"] ?? "redis";
var redisTag = builder.Configuration["ContainerImages:Redis:Tag"] ?? "7.4";

// =============================================================================
// Infrastructure Resources
// =============================================================================

// Redis for Orleans clustering (silo membership)
var redis = builder.AddRedis("orleans-clustering")
    .WithImage(redisImage, redisTag)
    .WithRedisInsight();

// Redis for rate limiting state (separate from clustering)
var rateLimitRedis = builder.AddRedis("rate-limiting")
    .WithImage(redisImage, redisTag);

// Database password
var dbPassword = builder.AddParameter("postgres-password");

// Database resource - PostgreSQL (returns both titan and titan-admin connections)
var (titanDb, titanAdminDb) = DatabaseResources.AddDatabase(builder, dbPassword, env);

// =============================================================================
// Orleans Cluster Configuration
// =============================================================================

var orleans = builder.AddOrleans("titan-cluster")
    .WithClustering(redis);

// =============================================================================
// Orleans Silo Hosts (with replicas for distributed testing)
// =============================================================================

// Get the current environment to propagate to child projects
var environment = builder.Environment.EnvironmentName;

// Configurable replica count for Orleans silos (default: 2)
var replicas = builder.Configuration.GetValue("Orleans:Replicas", 2);

var identityHost = builder.AddProject<Projects.Titan_IdentityHost>("identity-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);

// =============================================================================
// API Gateway (Orleans Client)
// =============================================================================

var api = builder.AddProject<Projects.Titan_API>("api")
    .WithReference(orleans.AsClient())
    .WithReference(titanDb)
    .WithReference(titanAdminDb)  // Admin Identity database for dashboard auth
    .WithReference(rateLimitRedis)
    .WaitFor(identityHost)  // Wait for at least one silo to be running
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", environment)
    .WithEnvironment("Jwt__Key", builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in AppHost configuration"))
    .WithEnvironment("RateLimiting__Enabled", builder.Configuration["RateLimiting:Enabled"] ?? "true");

// =============================================================================
// Admin Dashboard (React SPA via Vite)
// =============================================================================

var dashboard = builder.AddViteApp("dashboard", "../titan-dashboard")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

