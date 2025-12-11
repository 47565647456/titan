using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;

namespace Titan.AppHost;

/// <summary>
/// Helper class for database resource configuration in the Aspire AppHost.
/// New database backends can be added by implementing new static methods following the pattern.
/// </summary>
public static class DatabaseResources
{
    /// <summary>
    /// Adds the configured database resource to the Aspire application.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="password">The database password parameter.</param>
    /// <param name="env">The environment name (used for volume naming).</param>
    /// <param name="databaseType">The database type from configuration (e.g., "postgres").</param>
    /// <returns>A tuple containing the database resource and optional container (for custom containers).</returns>
    public static (IResourceBuilder<IResourceWithConnectionString> Database, IResourceBuilder<ContainerResource>? Container) AddDatabase(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        string env,
        string databaseType)
    {
        return databaseType switch
        {
            "postgres" => (AddPostgres(builder, password, env), null),
            "mongodb" => (AddMongoDB(builder, env), null),
            "mongodb-sharded" => AddMongoDBSharded(builder, env),
            _ => throw new NotSupportedException($"Database type '{databaseType}' is not supported. Supported types: postgres, mongodb, mongodb-sharded")
        };
    }

    /// <summary>
    /// Adds a PostgreSQL resource to the Aspire application.
    /// </summary>
    public static IResourceBuilder<IResourceWithConnectionString> AddPostgres(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> password,
        string env)
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

    /// <summary>
    /// Adds a MongoDB resource to the Aspire application.
    /// </summary>
    public static IResourceBuilder<IResourceWithConnectionString> AddMongoDB(
        IDistributedApplicationBuilder builder,
        string env)
    {
        Console.WriteLine("📦 Using MongoDB as database backend");

        var mongoImage = builder.Configuration["ContainerImages:MongoDB:Image"] ?? "mongo";
        var mongoTag = builder.Configuration["ContainerImages:MongoDB:Tag"] ?? "7";
        var persistContainer = builder.Configuration["Database:PersistContainer"]?.ToLowerInvariant() == "true";

        var mongo = builder.AddMongoDB("mongodb")
            .WithImage(mongoImage, mongoTag)
            .WithDataVolume($"titan-mongodb-data-{env}")
            .WithMongoExpress();  // Web UI for MongoDB administration
        
        if (persistContainer)
        {
            mongo.WithLifetime(ContainerLifetime.Persistent);  // Survives app shutdown for faster restarts
        }
        
        return mongo.AddDatabase("titan");
    }

    /// <summary>
    /// Adds a sharded MongoDB cluster for horizontal write scaling.
    /// Creates: 3 config servers, 2 shards (3 replicas each), 1 mongos router.
    /// </summary>
    public static (IResourceBuilder<IResourceWithConnectionString> Database, IResourceBuilder<ContainerResource>? Container) AddMongoDBSharded(
        IDistributedApplicationBuilder builder,
        string env)
    {
        Console.WriteLine("📦 Using MongoDB Sharded Cluster (10 containers)");

        var mongoImage = builder.Configuration["ContainerImages:MongoDB:Image"] ?? "mongo";
        var mongoTag = builder.Configuration["ContainerImages:MongoDB:Tag"] ?? "7";
        var persistContainer = builder.Configuration["Database:PersistContainer"]?.ToLowerInvariant() == "true";

        // =========================================================================
        // Config Server Replica Set (3 nodes)
        // =========================================================================
        var config1 = builder.AddContainer("mongo-config1", mongoImage, mongoTag)
            .WithArgs("--configsvr", "--replSet", "configReplSet", "--port", "27019", "--bind_ip_all")
            .WithVolume($"titan-mongo-config1-{env}", "/data/configdb");
        
        var config2 = builder.AddContainer("mongo-config2", mongoImage, mongoTag)
            .WithArgs("--configsvr", "--replSet", "configReplSet", "--port", "27019", "--bind_ip_all")
            .WithVolume($"titan-mongo-config2-{env}", "/data/configdb");
        
        var config3 = builder.AddContainer("mongo-config3", mongoImage, mongoTag)
            .WithArgs("--configsvr", "--replSet", "configReplSet", "--port", "27019", "--bind_ip_all")
            .WithVolume($"titan-mongo-config3-{env}", "/data/configdb");

        // =========================================================================
        // Shard 1 Replica Set (3 nodes)
        // =========================================================================
        var shard1a = builder.AddContainer("mongo-shard1a", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard1ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard1a-{env}", "/data/db");
        
        var shard1b = builder.AddContainer("mongo-shard1b", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard1ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard1b-{env}", "/data/db");
        
        var shard1c = builder.AddContainer("mongo-shard1c", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard1ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard1c-{env}", "/data/db");

        // =========================================================================
        // Shard 2 Replica Set (3 nodes)
        // =========================================================================
        var shard2a = builder.AddContainer("mongo-shard2a", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard2ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard2a-{env}", "/data/db");
        
        var shard2b = builder.AddContainer("mongo-shard2b", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard2ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard2b-{env}", "/data/db");
        
        var shard2c = builder.AddContainer("mongo-shard2c", mongoImage, mongoTag)
            .WithArgs("--shardsvr", "--replSet", "shard2ReplSet", "--port", "27018", "--bind_ip_all")
            .WithVolume($"titan-mongo-shard2c-{env}", "/data/db");

        // =========================================================================
        // Mongos Router (client connection point)
        // =========================================================================
        var mongos = builder.AddContainer("mongos", mongoImage, mongoTag)
            .WithEntrypoint("mongos")
            .WithArgs("--configdb", "configReplSet/mongo-config1:27019,mongo-config2:27019,mongo-config3:27019", "--bind_ip_all", "--port", "27017")
            .WithEndpoint(port: 27017, targetPort: 27017, name: "mongodb")
            .WaitFor(config1).WaitFor(config2).WaitFor(config3)
            .WaitFor(shard1a).WaitFor(shard1b).WaitFor(shard1c)
            .WaitFor(shard2a).WaitFor(shard2b).WaitFor(shard2c);

        if (persistContainer)
        {
            config1.WithLifetime(ContainerLifetime.Persistent);
            config2.WithLifetime(ContainerLifetime.Persistent);
            config3.WithLifetime(ContainerLifetime.Persistent);
            shard1a.WithLifetime(ContainerLifetime.Persistent);
            shard1b.WithLifetime(ContainerLifetime.Persistent);
            shard1c.WithLifetime(ContainerLifetime.Persistent);
            shard2a.WithLifetime(ContainerLifetime.Persistent);
            shard2b.WithLifetime(ContainerLifetime.Persistent);
            shard2c.WithLifetime(ContainerLifetime.Persistent);
            mongos.WithLifetime(ContainerLifetime.Persistent);
        }

        // =========================================================================
        // Mongo Express (Web UI for administration)
        // =========================================================================
        builder.AddContainer("mongo-express", "mongo-express", "latest")
            .WithEnvironment("ME_CONFIG_MONGODB_URL", "mongodb://mongos:27017")
            .WithEndpoint(port: 8081, targetPort: 8081, name: "http")
            .WithExternalHttpEndpoints()
            .WaitFor(mongos);

        // =========================================================================
        // Init Container (auto-configures replica sets and sharding)
        // =========================================================================
        var initContainer = builder.AddContainer("mongo-init", mongoImage, mongoTag)
            .WithEntrypoint("bash")
            .WithArgs("/scripts/init-cluster.sh")
            .WithBindMount("../../scripts/mongo-sharding", "/scripts")
            .WaitFor(mongos);  // Wait for all containers to be ready
        
        // Build connection string pointing to mongos router
        var mongosEndpoint = mongos.GetEndpoint("mongodb");
        var connectionString = builder.AddConnectionString(
            "titan",
            ReferenceExpression.Create(
                $"mongodb://{mongosEndpoint.Property(EndpointProperty.Host)}:{mongosEndpoint.Property(EndpointProperty.Port)}/titan"));

        return (connectionString, initContainer);
    }

    /// <summary>
    /// Checks if the default development password is being used in production.
    /// </summary>
    public static void CheckProductionPassword(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> dbPassword)
    {
        if (builder.Environment.IsProduction() &&
            builder.Configuration["Parameters:postgres-password"] == "TitanDevelopmentPassword123!")
        {
            Console.WriteLine("⚠️  WARNING: You are using the default development password in Production! Please set a secure password via the 'postgres-password' parameter.");
        }
    }

    /// <summary>
    /// Adds a wait dependency on a custom database container if applicable.
    /// For built-in Aspire resources like PostgreSQL, WaitFor(titanDb) handles it.
    /// </summary>
    public static void AddDbWait<T>(
        IResourceBuilder<T> project,
        IResourceBuilder<ContainerResource>? dbContainer)
        where T : IResourceWithWaitSupport
    {
        if (dbContainer != null)
        {
            project.WaitFor(dbContainer);
        }
    }
}
