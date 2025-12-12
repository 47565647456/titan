using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;
using Titan.ServiceDefaults.Storage;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Orleans grain storage.
/// Centralizes database-specific configuration so silo hosts remain database-agnostic.
/// </summary>
public static class GrainStorageExtensions
{
    /// <summary>
    /// Configures Orleans grain storage with regional and global providers.
    /// - OrleansStorage: Uses CockroachDB (titan) with retry logic
    /// - GlobalStorage: Uses CockroachDB (titan) with retry logic
    /// - TransactionStore: Uses CockroachDB (titan) with retry logic
    /// </summary>
    public static ISiloBuilder AddTitanGrainStorage(this ISiloBuilder silo, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("titan");

        // Use the same storage configuration for all providers since we are moving to a single global DB
        // Always wrap with retry logic for CockroachDB serializable transaction conflicts
        AddRetryingAdoNetStorage(silo, "OrleansStorage", connectionString);
        AddRetryingAdoNetStorage(silo, "TransactionStore", connectionString);
        AddRetryingAdoNetStorage(silo, "GlobalStorage", connectionString);
        AddRetryingAdoNetStorage(silo, "PubSubStore", connectionString); // Persistent storage for streams

        return silo;
    }

    private static void AddRetryingAdoNetStorage(ISiloBuilder silo, string name, string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine($"📦 Uses in-memory storage for '{name}' (no connection string)");
            silo.AddMemoryGrainStorage(name);
            return;
        }

        Console.WriteLine($"📦 Configuring CockroachDB storage for '{name}' with retry logic");

        // Register the underlying ADO.NET storage with a temporary name
        var innerStorageName = $"{name}_Inner";
        silo.AddAdoNetGrainStorage(innerStorageName, options =>
        {
            options.Invariant = "Npgsql";  // CockroachDB is PostgreSQL-compatible
            options.ConnectionString = connectionString;
        });

        // Wrap with retry logic for CockroachDB serialization conflicts
        silo.Services.AddKeyedSingleton<IGrainStorage>(name, (sp, key) =>
        {
            var innerStorage = sp.GetRequiredKeyedService<IGrainStorage>(innerStorageName);
            return new RetryingGrainStorage(innerStorage);
        });
    }
}
