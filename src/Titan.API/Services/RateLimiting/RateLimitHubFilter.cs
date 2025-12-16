using Microsoft.AspNetCore.SignalR;

namespace Titan.API.Services.RateLimiting;

/// <summary>
/// SignalR Hub filter that rate limits individual hub method invocations.
/// Partition key: UserId (authenticated) or ConnectionId (anonymous).
/// </summary>
public class RateLimitHubFilter : IHubFilter
{
    private readonly RateLimitService _rateLimitService;
    private readonly ILogger<RateLimitHubFilter> _logger;

    public RateLimitHubFilter(RateLimitService rateLimitService, ILogger<RateLimitHubFilter> logger)
    {
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            // Skip rate limiting if disabled
            if (!await _rateLimitService.IsEnabledAsync())
            {
                return await next(invocationContext);
            }

            var hubName = invocationContext.Hub.GetType().Name;
            var methodName = invocationContext.HubMethodName;
            var endpoint = $"{hubName}.{methodName}";

            // Get partition key: user ID for authenticated, connection ID for anonymous
            var userId = invocationContext.Context.UserIdentifier;
            var partitionKey = !string.IsNullOrEmpty(userId)
                ? $"user:{userId}"
                : $"conn:{invocationContext.Context.ConnectionId}";

            // Get policy for this endpoint
            var policy = await _rateLimitService.GetPolicyForEndpointAsync(endpoint);
            if (policy == null)
            {
                return await next(invocationContext);
            }

            // Check rate limit
            var result = await _rateLimitService.CheckAsync(partitionKey, policy.Name);

            if (!result.IsAllowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for {PartitionKey} on {Endpoint}. Policy: {Policy}, RetryAfter: {RetryAfter}s",
                    partitionKey, endpoint, policy.Name, result.RetryAfterSeconds);

                // Send rate limit info to client before throwing
                try
                {
                    await invocationContext.Hub.Clients.Caller.SendAsync("RateLimitExceeded", new
                    {
                        Policy = policy.Name,
                        RetryAfterSeconds = result.RetryAfterSeconds,
                        State = result.GetStateHeaderValue()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send RateLimitExceeded notification to client");
                }

                throw new HubException($"Rate limit exceeded. Retry after {result.RetryAfterSeconds} seconds.");
            }
        }
        catch (HubException)
        {
            // Re-throw rate limit exceptions
            throw;
        }
        catch (Exception ex)
        {
            // If rate limiting fails (Redis/Orleans unavailable), allow the request through
            _logger.LogWarning(ex, "Rate limiting check failed for hub method, allowing request through");
        }

        return await next(invocationContext);
    }
}
