using Microsoft.Extensions.DependencyInjection;

namespace Titan.API.Services.Auth;

/// <summary>
/// Factory implementation that resolves authentication services from keyed DI registrations.
/// </summary>
public class AuthServiceFactory : IAuthServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;
    private readonly Dictionary<string, IAuthService> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public AuthServiceFactory(IServiceProvider serviceProvider, IHostEnvironment environment)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
    }

    public IAuthService GetService(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be empty.", nameof(providerName));

        using (_lock.EnterScope())
        {
            if (_cache.TryGetValue(providerName, out var cached))
                return cached;

            var service = _serviceProvider.GetKeyedService<IAuthService>(providerName);
            if (service == null)
            {
                var available = string.Join(", ", GetProviderNames());
                throw new ArgumentException(
                    $"Authentication provider '{providerName}' is not registered. Available providers: {available}",
                    nameof(providerName));
            }

            _cache[providerName] = service;
            return service;
        }
    }

    public IAuthService GetDefaultService()
    {
        // In development, prefer Mock for easier testing
        // In production, always use EOS
        if (_environment.IsDevelopment() && HasProvider("Mock"))
        {
            return GetService("Mock");
        }

        return GetService("EOS");
    }

    public IEnumerable<string> GetProviderNames()
    {
        // Check for known providers
        var providers = new List<string>();
        
        if (_serviceProvider.GetKeyedService<IAuthService>("EOS") != null)
            providers.Add("EOS");
        
        if (_serviceProvider.GetKeyedService<IAuthService>("Mock") != null)
            providers.Add("Mock");
        
        return providers;
    }

    public bool HasProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;

        using (_lock.EnterScope())
        {
            if (_cache.ContainsKey(providerName))
                return true;
        }

        return _serviceProvider.GetKeyedService<IAuthService>(providerName) != null;
    }
}
