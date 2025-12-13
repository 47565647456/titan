namespace Titan.Client;

/// <summary>
/// Fluent builder for creating TitanClient instances.
/// </summary>
public sealed class TitanClientBuilder
{
    private readonly TitanClientOptions _options = new();

    /// <summary>
    /// Set the base URL of the Titan API.
    /// </summary>
    public TitanClientBuilder WithBaseUrl(string baseUrl)
    {
        _options.BaseUrl = baseUrl.TrimEnd('/');
        return this;
    }

    /// <summary>
    /// Configure logging for the client.
    /// </summary>
    public TitanClientBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        _options.LoggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Set the connection timeout for SignalR hub connections.
    /// </summary>
    public TitanClientBuilder WithConnectionTimeout(TimeSpan timeout)
    {
        _options.ConnectionTimeout = timeout;
        return this;
    }

    /// <summary>
    /// Enable or disable automatic reconnection for SignalR hubs.
    /// </summary>
    public TitanClientBuilder WithAutoReconnect(bool enabled = true)
    {
        _options.EnableAutoReconnect = enabled;
        return this;
    }

    /// <summary>
    /// Build the TitanClient with the configured options.
    /// </summary>
    public TitanClient Build()
    {
        if (string.IsNullOrEmpty(_options.BaseUrl))
            throw new InvalidOperationException("BaseUrl must be configured. Call WithBaseUrl().");

        return new TitanClient(_options);
    }
}
