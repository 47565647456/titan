using Npgsql;
using Orleans.Storage;
using Polly;
using Polly.Retry;

namespace Titan.ServiceDefaults.Storage;

/// <summary>
/// Wraps an IGrainStorage with retry logic for CockroachDB serialization conflicts.
/// CockroachDB recommends client-side retry for SQLSTATE 40001 errors.
/// </summary>
public class RetryingGrainStorage : IGrainStorage
{
    private readonly IGrainStorage _inner;
    private readonly ResiliencePipeline _pipeline;

    public RetryingGrainStorage(IGrainStorage inner, RetryOptions? options = null)
    {
        _inner = inner;
        options ??= new RetryOptions();

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<PostgresException>(ex => 
                    ex.SqlState == "40001"), // Serialization failure
                MaxRetryAttempts = options.MaxRetries,
                Delay = options.InitialDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    Console.WriteLine($"🔄 CockroachDB retry {args.AttemptNumber}/{options.MaxRetries} " +
                                      $"after {args.RetryDelay.TotalMilliseconds}ms");
                    return default;
                }
            })
            .Build();
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await _pipeline.ExecuteAsync(async _ => 
            await _inner.ReadStateAsync(stateName, grainId, grainState));
    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await _pipeline.ExecuteAsync(async _ => 
            await _inner.WriteStateAsync(stateName, grainId, grainState));
    }

    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        await _pipeline.ExecuteAsync(async _ => 
            await _inner.ClearStateAsync(stateName, grainId, grainState));
    }
}

/// <summary>
/// Configuration options for CockroachDB transaction retry.
/// </summary>
public class RetryOptions
{
    /// <summary>Maximum number of retry attempts.</summary>
    public int MaxRetries { get; set; } = 5;
    
    /// <summary>Initial delay before first retry (exponential backoff).</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(50);
}
