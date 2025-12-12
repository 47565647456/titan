using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;

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

        // Register the codec as a singleton and expose it through all required interfaces
        services.AddSingleton<MemoryPackCodec>();
        services.AddSingleton<IGeneralizedCodec>(sp => sp.GetRequiredService<MemoryPackCodec>());
        services.AddSingleton<IGeneralizedCopier>(sp => sp.GetRequiredService<MemoryPackCodec>());
        services.AddSingleton<ITypeFilter>(sp => sp.GetRequiredService<MemoryPackCodec>());

        return builder;
    }
}
