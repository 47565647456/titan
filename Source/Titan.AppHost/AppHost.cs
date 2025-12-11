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

// Database password - using a stable password for local dev
var dbPassword = builder.AddParameter("postgres-password", secret: true);
DatabaseResources.CheckProductionPassword(builder, dbPassword);

// Database resource - extensible via Database:Type configuration
var databaseType = builder.Configuration["Database:Type"]?.ToLowerInvariant() ?? "postgres";
var (titanDb, dbContainer) = DatabaseResources.AddDatabase(builder, dbPassword, env, databaseType);

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

var identityHost = builder.AddProject<Projects.Titan_IdentityHost>("identity-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);
DatabaseResources.AddDbWait(identityHost, dbContainer);

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);
DatabaseResources.AddDbWait(inventoryHost, dbContainer);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);
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

builder.Build().Run();
