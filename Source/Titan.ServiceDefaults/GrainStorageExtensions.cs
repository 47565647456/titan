using Microsoft.Extensions.Configuration;
using Orleans.Providers.MongoDB.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Orleans grain storage.
/// Centralizes database-specific configuration so silo hosts remain database-agnostic.
/// </summary>
public static class GrainStorageExtensions
{
    /// <summary>
    /// Configures Orleans grain storage based on Database:Type configuration.
    /// Supports: postgres, mongodb, mongodb-sharded, or falls back to memory storage.
    /// </summary>
    public static ISiloBuilder AddTitanGrainStorage(this ISiloBuilder silo, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("titan");
        var dbType = config["Database:Type"]?.ToLowerInvariant() ?? "postgres";

        if (string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("📦 Using in-memory grain storage (no connection string)");
            return silo
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("TransactionStore");
        }

        return dbType switch
        {
            "mongodb" or "mongodb-sharded" => silo.AddMongoDBStorage(connectionString),
            _ => silo.AddPostgresStorage(connectionString) // postgres is default
        };
    }

    private static ISiloBuilder AddMongoDBStorage(this ISiloBuilder silo, string connectionString)
    {
        Console.WriteLine("📦 Configuring MongoDB grain storage (standalone or sharded)");
        
        return silo
            .UseMongoDBClient(connectionString)
            .AddMongoDBGrainStorage("OrleansStorage", options =>
            {
                options.DatabaseName = "titan";
            })
            .AddMongoDBGrainStorage("TransactionStore", options =>
            {
                options.DatabaseName = "titan";
            });
    }

    private static ISiloBuilder AddPostgresStorage(this ISiloBuilder silo, string connectionString)
    {
        Console.WriteLine("📦 Configuring PostgreSQL grain storage");
        
        return silo
            .AddAdoNetGrainStorage("OrleansStorage", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = connectionString;
            })
            .AddAdoNetGrainStorage("TransactionStore", options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = connectionString;
            });
    }
}
