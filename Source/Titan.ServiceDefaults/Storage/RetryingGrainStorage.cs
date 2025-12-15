using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orleans.Storage;
using Polly;
using Polly.Timeout;

namespace Titan.ServiceDefaults.Storage;

/// <summary>
/// Wraps an IGrainStorage with retry logic for PostgreSQL/CockroachDB transient errors.
/// Both databases signal retry errors with SQLSTATE 40001 for serialization conflicts.
/// CockroachDB: https://www.cockroachlabs.com/docs/stable/transaction-retry-error-reference
/// PostgreSQL: https://www.postgresql.org/docs/current/errcodes-appendix.html
/// </summary>
public class RetryingGrainStorage : IGrainStorage
{
    private readonly IGrainStorage _inner;
    private readonly ILogger<RetryingGrainStorage> _logger;
    private readonly ResiliencePipeline _writePipeline;

    // PostgreSQL/CockroachDB transient error SQL states
    // SQLSTATE 40001: Serialization failure / transaction retry
    // SQLSTATE 08006: Connection failure
    // SQLSTATE 57P01: Admin shutdown
    private static readonly HashSet<string> RetryableSqlStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "40001", // Serialization failure / transaction retry (PostgreSQL and CockroachDB)
        "08006", // Connection failure
        "57P01", // Admin shutdown
    };

    public RetryingGrainStorage(
        IGrainStorage inner, 
        ILogger<RetryingGrainStorage>? logger = null,
        RetryOptions? options = null)
    {
        _inner = inner;
        _logger = logger ?? NullLogger<RetryingGrainStorage>.Instance;
        options ??= new RetryOptions();

        _writePipeline = new ResiliencePipelineBuilder()
            .AddTimeout(options.OperationTimeout)
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<PostgresException>(IsRetryable)
                    .Handle<TimeoutRejectedException>(),
                MaxRetryAttempts = options.MaxRetries,
                Delay = options.InitialDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    var exception = args.Outcome.Exception;
                    var sqlState = (exception as PostgresException)?.SqlState ?? "timeout";
                    
                    _logger.LogWarning(
                        "Database retry {Attempt}/{MaxAttempts} for {SqlState} after {Delay}ms",
                        args.AttemptNumber,
                        options.MaxRetries,
                        sqlState,
                        args.RetryDelay.TotalMilliseconds);
                    
                    return default;
                }
            })
            .Build();
    }

    private static bool IsRetryable(PostgresException ex) => 
        RetryableSqlStates.Contains(ex.SqlState ?? string.Empty);

    /// <summary>
    /// Reads are generally safe and don't need retry for serialization conflicts.
    /// Pass through directly to avoid overhead.
    /// </summary>
    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
        => _inner.ReadStateAsync(stateName, grainId, grainState);

    /// <summary>
    /// Writes may conflict under high concurrency. Apply retry with exponential backoff.
    /// </summary>
    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await _writePipeline.ExecuteAsync(
            async ct => await _inner.WriteStateAsync(stateName, grainId, grainState),
            CancellationToken.None);
    }

    /// <summary>
    /// Clears may conflict under high concurrency. Apply retry with exponential backoff.
    /// </summary>
    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await _writePipeline.ExecuteAsync(
            async ct => await _inner.ClearStateAsync(stateName, grainId, grainState),
            CancellationToken.None);
    }
}

/// <summary>
/// Configuration options for database transaction retry (PostgreSQL/CockroachDB).
/// Can be configured via appsettings.json under Database:Retry section.
/// </summary>
public class RetryOptions
{
    /// <summary>Maximum number of retry attempts for write operations.</summary>
    public int MaxRetries { get; set; } = 5;
    
    /// <summary>Initial delay in milliseconds before first retry (exponential backoff with jitter).</summary>
    public int InitialDelayMs { get; set; } = 50;
    
    /// <summary>Maximum time in seconds for a single operation before timeout.</summary>
    public int OperationTimeoutSeconds { get; set; } = 30;
    
    /// <summary>Gets the initial delay as a TimeSpan.</summary>
    public TimeSpan InitialDelay => TimeSpan.FromMilliseconds(InitialDelayMs);
    
    /// <summary>Gets the operation timeout as a TimeSpan.</summary>
    public TimeSpan OperationTimeout => TimeSpan.FromSeconds(OperationTimeoutSeconds);
}
