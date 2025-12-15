using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Titan.ServiceDefaults.Serialization;
using Titan.ServiceDefaults.Storage;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Orleans grain storage.
/// Centralizes database-specific configuration so silo hosts remain database-agnostic.
/// </summary>
public static class GrainStorageExtensions
{
    /// <summary>
    /// Configures Orleans grain storage providers backed by PostgreSQL-compatible database.
    /// All providers use the same connection with retry logic for transient errors.
    /// Supports both PostgreSQL and CockroachDB (PostgreSQL wire protocol compatible).
    /// </summary>
    public static ISiloBuilder AddTitanGrainStorage(this ISiloBuilder silo, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("titan");

        // Use MemoryPack for application grain storage (faster, smaller payloads)
        AddRetryingAdoNetStorage(silo, "OrleansStorage", connectionString, config, useMemoryPack: true);
        AddRetryingAdoNetStorage(silo, "GlobalStorage", connectionString, config, useMemoryPack: true);
        
        // PubSubStore and TransactionStore use JSON - Orleans internal types are not MemoryPackable
        AddRetryingAdoNetStorage(silo, "PubSubStore", connectionString, config, useMemoryPack: false);
        AddRetryingAdoNetStorage(silo, "TransactionStore", connectionString, config, useMemoryPack: false);

        return silo;
    }

    private static void AddRetryingAdoNetStorage(
        ISiloBuilder silo, 
        string name, 
        string? connectionString, 
        IConfiguration config,
        bool useMemoryPack = true)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            // Fall back to in-memory storage when no database is configured
            silo.AddMemoryGrainStorage(name);
            return;
        }

        // Register the underlying ADO.NET storage with a prefixed name
        var innerStorageName = $"{name}_Inner";
        silo.AddAdoNetGrainStorage(innerStorageName, options =>
        {
            options.Invariant = "Npgsql";  // PostgreSQL wire protocol (works with PostgreSQL and CockroachDB)
            options.ConnectionString = connectionString;
            
            // Use MemoryPack for faster binary serialization, or System.Text.Json for fallback
            // (e.g., TransactionStore uses Orleans internal types that may not be MemoryPackable)
            options.GrainStorageSerializer = useMemoryPack
                ? MemoryPackSerializerExtensions.CreateMemoryPackGrainStorageSerializer()
                : MemoryPackSerializerExtensions.CreateSystemTextJsonGrainStorageSerializer();
        });

        // Wrap with retry logic for PostgreSQL/CockroachDB transient errors (serialization conflicts, connection failures)
        silo.Services.AddKeyedSingleton<IGrainStorage>(name, (sp, key) =>
        {
            var innerStorage = sp.GetRequiredKeyedService<IGrainStorage>(innerStorageName);
            var logger = sp.GetRequiredService<ILogger<RetryingGrainStorage>>();
            
            var retryOptions = new RetryOptions();
            config.GetSection("Database:Retry").Bind(retryOptions);
            
            return new RetryingGrainStorage(innerStorage, logger, retryOptions);
        });
    }
}
