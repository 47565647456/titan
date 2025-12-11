using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// =============================================================================
// Infrastructure Resources
// =============================================================================

// Redis for Orleans clustering (silo membership)
var redis = builder.AddRedis("orleans-clustering")
    .WithRedisInsight();

// MongoDB or Postgres password - using a stable password for local dev to avoid
// authentication errors when the container restarts with a persistent volume.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

if (builder.Environment.IsProduction() && 
    builder.Configuration["Parameters:postgres-password"] == "TitanDevelopmentPassword123!")
{
    Console.WriteLine("⚠️  WARNING: You are using the default development password for Postgres in Production! Please set a secure password via the 'postgres-password' parameter.");
}

// PostgreSQL for Orleans grain persistence
// WithInitFiles runs init-orleans-db.sql to create the schema
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithPgAdmin()
    .WithInitFiles("../../scripts");

var postgresVolume = builder.Configuration["PostgresVolume"];
if (string.IsNullOrEmpty(postgresVolume))
{
    // Default to persistent volume for local dev
    postgres.WithDataVolume("titan-postgres-dev");
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

// Get the current environment to propagate to child projects
// This is critical for integration tests where launch profiles are not used
var environment = builder.Environment.EnvironmentName;

var identityHost = builder.AddProject<Projects.Titan_IdentityHost>("identity-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);  // Multiple silos for distributed testing

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);

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
