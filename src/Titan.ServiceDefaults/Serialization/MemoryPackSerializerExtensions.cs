using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;
using Orleans.Storage;

namespace Titan.ServiceDefaults.Serialization;

/// <summary>
/// Extension methods for registering MemoryPack serialization with Orleans.
/// </summary>
public static class MemoryPackSerializerExtensions
{
    /// <summary>
    /// Adds MemoryPack serialization support to Orleans.
    /// Types decorated with [MemoryPackable] will be serialized using MemoryPack.
    /// </summary>
    /// <param name="builder">The serializer builder.</param>
    /// <param name="configureOptions">Optional action to configure MemoryPack options.</param>
    /// <returns>The serializer builder for chaining.</returns>
    public static ISerializerBuilder AddMemoryPackSerializer(
        this ISerializerBuilder builder,
        Action<MemoryPackCodecOptions>? configureOptions = null)
    {
        var services = builder.Services;

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            // Ensure options are registered even without configuration
            services.Configure<MemoryPackCodecOptions>(_ => { });
        }

        // Configure ExceptionSerializationOptions to allow exceptions to be serialized
        // by Orleans' built-in ExceptionCodec (fix for GitHub issue dotnet/orleans#8201)
        services.Configure<ExceptionSerializationOptions>(options =>
        {
            options.SupportedNamespacePrefixes.Add("MemoryPack");
            options.SupportedNamespacePrefixes.Add("Npgsql");        // PostgreSQL driver exceptions
            options.SupportedNamespacePrefixes.Add("System.Net");    // Network exceptions
            options.SupportedNamespacePrefixes.Add("System.IO");     // IO exceptions
        });

        // Register the codec as a singleton and expose it through all required interfaces
        services.AddSingleton<MemoryPackCodec>();
        services.AddSingleton<IGeneralizedCodec>(sp => sp.GetRequiredService<MemoryPackCodec>());
        services.AddSingleton<IGeneralizedCopier>(sp => sp.GetRequiredService<MemoryPackCodec>());
        services.AddSingleton<ITypeFilter>(sp => sp.GetRequiredService<MemoryPackCodec>());

        return builder;
    }

    /// <summary>
    /// Creates a MemoryPack grain storage serializer for use with ADO.NET grain storage.
    /// </summary>
    /// <returns>A new instance of <see cref="MemoryPackGrainStorageSerializer"/>.</returns>
    public static IGrainStorageSerializer CreateMemoryPackGrainStorageSerializer()
        => new MemoryPackGrainStorageSerializer();

    /// <summary>
    /// Creates a System.Text.Json grain storage serializer for use with ADO.NET grain storage.
    /// Used for storage providers that need JSON (e.g., TransactionStore with Orleans internals).
    /// </summary>
    /// <returns>A new instance of <see cref="SystemTextJsonGrainStorageSerializer"/>.</returns>
    public static IGrainStorageSerializer CreateSystemTextJsonGrainStorageSerializer()
        => new SystemTextJsonGrainStorageSerializer();
}
