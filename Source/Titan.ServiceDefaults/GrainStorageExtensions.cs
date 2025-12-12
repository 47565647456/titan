using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Orleans grain storage.
/// Centralizes database-specific configuration so silo hosts remain database-agnostic.
/// </summary>
public static class GrainStorageExtensions
{
    /// <summary>
    /// Configures Orleans grain storage based on Database:Type configuration.
    /// Supports: postgres, or falls back to memory storage.
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
            "postgres" => silo.AddPostgresStorage(connectionString),
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported. Supported types: postgres")
        };
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
