var builder = DistributedApplication.CreateBuilder(args);

// =============================================================================
// Infrastructure Resources
// =============================================================================

// Redis for Orleans clustering (silo membership)
var redis = builder.AddRedis("orleans-clustering")
    .WithRedisInsight();

// PostgreSQL for Orleans grain persistence
// WithInitFiles runs init-orleans-db.sql to create the schema
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithInitFiles("../../scripts");

var postgresVolume = builder.Configuration["PostgresVolume"];
if (string.IsNullOrEmpty(postgresVolume))
{
    // Default to persistent volume for local dev
    postgres.WithDataVolume("titan-postgres-data");
}
else if (postgresVolume.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) || 
         postgresVolume.Equals("none", StringComparison.OrdinalIgnoreCase))
{
    // No volume - ephemeral (clean db every time)
}
else
{
    // Use specified volume name
    postgres.WithDataVolume(postgresVolume);
}

var titanDb = postgres.AddDatabase("titan");

// =============================================================================
// Orleans Cluster Configuration
// =============================================================================

var orleans = builder.AddOrleans("titan-cluster")
    .WithClustering(redis);

// =============================================================================
// Orleans Silo Hosts (with replicas for distributed testing)
// =============================================================================

var identityHost = builder.AddProject<Projects.Titan_IdentityHost>("identity-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithReplicas(2);  // Multiple silos for distributed testing

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithReplicas(2);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithReplicas(2);

// =============================================================================
// API Gateway (Orleans Client)
// =============================================================================

var api = builder.AddProject<Projects.Titan_API>("api")
    .WithReference(orleans.AsClient())
    .WithReference(titanDb)
    .WaitFor(identityHost)  // Wait for at least one silo to be running
    .WithExternalHttpEndpoints()
    .WithEnvironment("Jwt__Key", builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing in AppHost configuration"))
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");  // Enable detailed errors

builder.Build().Run();
