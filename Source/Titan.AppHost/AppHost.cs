using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;

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
CheckProductionPassword(builder, dbPassword);

// Database resource - PostgreSQL or YugabyteDB based on configuration
IResourceBuilder<IResourceWithConnectionString> titanDb;
IResourceBuilder<ContainerResource>? yugabyteContainer = null;

var databaseType = builder.Configuration["Database:Type"]?.ToLowerInvariant() ?? "postgres";

if (databaseType == "yugabyte")
{
    (titanDb, yugabyteContainer) = AddYugabyte(builder, env);
}
else
{
    titanDb = AddPostgres(builder, dbPassword, env);
}

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
AddDbWait(identityHost, yugabyteContainer);

var inventoryHost = builder.AddProject<Projects.Titan_InventoryHost>("inventory-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);
AddDbWait(inventoryHost, yugabyteContainer);

var tradingHost = builder.AddProject<Projects.Titan_TradingHost>("trading-host")
    .WithReference(orleans)
    .WithReference(titanDb)
    .WaitFor(titanDb)
    .WithEnvironment("DOTNET_ENVIRONMENT", environment)
    .WithReplicas(2);
AddDbWait(tradingHost, yugabyteContainer);

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

// =============================================================================
// Helper Functions
// =============================================================================

static void CheckProductionPassword(IDistributedApplicationBuilder builder, IResourceBuilder<ParameterResource> dbPassword)
{
    if (builder.Environment.IsProduction() && 
        builder.Configuration["Parameters:postgres-password"] == "TitanDevelopmentPassword123!")
    {
        Console.WriteLine("⚠️  WARNING: You are using the default development password in Production! Please set a secure password via the 'postgres-password' parameter.");
    }
}

static (IResourceBuilder<IResourceWithConnectionString> Db, IResourceBuilder<ContainerResource> Container) AddYugabyte(
    IDistributedApplicationBuilder builder, string env)
{
    Console.WriteLine("📦 Using YugabyteDB as database backend");

    var yugabyteImage = builder.Configuration["ContainerImages:Yugabyte:Image"] ?? "yugabytedb/yugabyte";
    var yugabyteTag = builder.Configuration["ContainerImages:Yugabyte:Tag"] ?? "2025.1.2.1-b4";
    var scriptsPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../scripts/yuga"));

    var container = builder.AddContainer("yugabyte", yugabyteImage, yugabyteTag)
        .WithEndpoint(port: 5433, targetPort: 5433, name: "ysql")  // PostgreSQL-compatible port
        .WithEndpoint(port: 7000, targetPort: 7000, name: "master-ui")
        .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "tserver-ui")
        .WithHttpEndpoint(port: 15433, targetPort: 15433, name: "yugabyte-ui")
        .WithBindMount(scriptsPath, "/docker-entrypoint-initdb.d", isReadOnly: true)
        // Set a static hostname to avoid "Name or service not known" errors on restart, required for YugabyteDB. How does this work with docker and aspire scaling?
        .WithContainerRuntimeArgs("--hostname", "yugabyte")
        .WithArgs("bin/yugabyted", "start", "--background=false", "--initial_scripts_dir=/docker-entrypoint-initdb.d");

    var volumeName = builder.Configuration["Database:Volume"];
    if (string.IsNullOrEmpty(volumeName))
    {
        container.WithVolume($"titan-yugabyte-data-{env}", "/root/var");
    }
    else if (!volumeName.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) &&
             !volumeName.Equals("none", StringComparison.OrdinalIgnoreCase))
    {
        container.WithVolume(volumeName, "/root/var");
    }

    var ysqlEndpoint = container.GetEndpoint("ysql");
    var db = builder.AddConnectionString(
        "titan",
        ReferenceExpression.Create(
            $"Host={ysqlEndpoint.Property(EndpointProperty.Host)};Port={ysqlEndpoint.Property(EndpointProperty.Port)};Database=titan;Username=yugabyte;Password=yugabyte"));

    // Custom health check for YugabyteDB initialization
    // This should be more robust
    var startTime = DateTime.UtcNow;
    builder.Services.AddHealthChecks()
        .AddCheck("yugabyte-init", () =>
        {
            var elapsed = DateTime.UtcNow - startTime;
            return elapsed.TotalSeconds >= 30
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("YugabyteDB initialization complete")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"Waiting for YugabyteDB initialization ({elapsed.TotalSeconds:F0}s / 30s)");
        });

    container.WithHealthCheck("yugabyte-init");

    return (db, container);
}

static IResourceBuilder<IResourceWithConnectionString> AddPostgres(
    IDistributedApplicationBuilder builder, IResourceBuilder<ParameterResource> password, string env)
{
    Console.WriteLine("📦 Using PostgreSQL as database backend");

    var postgresImage = builder.Configuration["ContainerImages:Postgres:Image"] ?? "postgres";
    var postgresTag = builder.Configuration["ContainerImages:Postgres:Tag"] ?? "17";

    var postgres = builder.AddPostgres("postgres", password: password)
        .WithImage(postgresImage, postgresTag)
        .WithPgAdmin()
        .WithInitFiles("../../scripts");

    var volumeName = builder.Configuration["Database:Volume"];
    if (string.IsNullOrEmpty(volumeName))
    {
        postgres.WithDataVolume($"titan-postgres-data-{env}");
    }
    else if (!volumeName.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) && 
             !volumeName.Equals("none", StringComparison.OrdinalIgnoreCase))
    {
        postgres.WithDataVolume(volumeName);
    }

    return postgres.AddDatabase("titan");
}

static void AddDbWait<T>(IResourceBuilder<T> project, IResourceBuilder<ContainerResource>? yugabyteContainer) 
    where T : IResourceWithWaitSupport
{
    // For YugabyteDB, we wait on the container; for PostgreSQL, WaitFor(titanDb) handles it
    if (yugabyteContainer != null)
    {
        project.WaitFor(yugabyteContainer);
    }
}
