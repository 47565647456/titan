using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Titan.Client;

/// <summary>
/// Extension methods for registering TitanClient with dependency injection.
/// </summary>
public static class TitanClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds TitanClient to the service collection with the specified configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action for TitanClientOptions.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTitanClient(
        this IServiceCollection services,
        Action<TitanClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TitanClientOptions>>().Value;
            return new TitanClient(options);
        });
        return services;
    }

    /// <summary>
    /// Adds TitanClient to the service collection with a pre-configured options instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The pre-configured options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTitanClient(
        this IServiceCollection services,
        TitanClientOptions options)
    {
        services.AddSingleton(new TitanClient(options));
        return services;
    }
}
