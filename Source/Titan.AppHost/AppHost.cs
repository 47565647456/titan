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

// Database credentials - using a stable password for local dev
// Parameters support both PostgreSQL and CockroachDB (configured via Database:Type)
var dbPassword = builder.AddParameter("cockroachdb-password");  // Name kept for backwards compatibility
var dbUsername = builder.AddParameter("cockroachdb-username");  // Name kept for backwards compatibility

// Database resource - PostgreSQL or CockroachDB (returns both titan and titan-admin connections)
var (titanDb, titanAdminDb, dbContainer) = DatabaseResources.AddDatabase(builder, dbPassword, dbUsername, env);

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
DatabaseResources.AddDbWait(identityHost, dbContainer);

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);
DatabaseResources.AddDbWait(inventoryHost, dbContainer);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(replicas);
DatabaseResources.AddDbWait(tradingHost, dbContainer);

// =============================================================================
// API Gateway (Orleans Client)
// =============================================================================

var api = builder.AddProject<Projects.Titan_API>("api")
    .WithReference(orleans.AsClient())
    .WithReference(titanDb)
    .WaitFor(identityHost)  // Wait for at least one silo to be running
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", environment)
    .WithEnvironment("Jwt__Key", builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in AppHost configuration"));

// =============================================================================
// Admin Dashboard (Orleans Client + Identity)
// =============================================================================

var dashboard = builder.AddProject<Projects.Titan_Dashboard>("dashboard")
    .WithReference(orleans.AsClient())
    .WithReference(titanDb)       // Orleans storage database (for account queries)
    .WithReference(titanAdminDb)  // Admin Identity database (titan_admin)
    .WaitFor(identityHost)  // Wait for at least one silo to be running
    .WithExternalHttpEndpoints()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", environment);
DatabaseResources.AddDbWait(dashboard, dbContainer);  // Wait for admin db init

builder.Build().Run();

