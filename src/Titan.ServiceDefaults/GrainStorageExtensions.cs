using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Titan.ServiceDefaults.Serialization;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Orleans grain storage.
/// Centralizes database-specific configuration so silo hosts remain database-agnostic.
/// </summary>
public static class GrainStorageExtensions
{
    /// <summary>
    /// Configures Orleans grain storage providers backed by PostgreSQL.
    /// All providers use the same connection string.
    /// </summary>
    public static ISiloBuilder AddTitanGrainStorage(this ISiloBuilder silo, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("titan");

        // Use MemoryPack for application grain storage (faster, smaller payloads)
        AddAdoNetStorageWithConfig(silo, "OrleansStorage", connectionString, useMemoryPack: true);
        AddAdoNetStorageWithConfig(silo, "GlobalStorage", connectionString, useMemoryPack: true);
        
        // PubSubStore and TransactionStore use JSON - Orleans internal types are not MemoryPackable
        AddAdoNetStorageWithConfig(silo, "PubSubStore", connectionString, useMemoryPack: false);
        AddAdoNetStorageWithConfig(silo, "TransactionStore", connectionString, useMemoryPack: false);

        return silo;
    }

    private static void AddAdoNetStorageWithConfig(
        ISiloBuilder silo, 
        string name, 
        string? connectionString,
        bool useMemoryPack = true)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            // Fall back to in-memory storage when no database is configured
            silo.AddMemoryGrainStorage(name);
            return;
        }

        // Register ADO.NET storage with PostgreSQL
        silo.AddAdoNetGrainStorage(name, options =>
        {
            options.Invariant = "Npgsql";  // PostgreSQL
            options.ConnectionString = connectionString;
            
            // Use MemoryPack for faster binary serialization, or System.Text.Json for fallback
            // (e.g., TransactionStore uses Orleans internal types that may not be MemoryPackable)
            options.GrainStorageSerializer = useMemoryPack
                ? MemoryPackSerializerExtensions.CreateMemoryPackGrainStorageSerializer()
                : MemoryPackSerializerExtensions.CreateSystemTextJsonGrainStorageSerializer();
        });
    }
}
