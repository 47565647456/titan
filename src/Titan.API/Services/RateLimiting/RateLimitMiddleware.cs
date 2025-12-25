using System.Security.Claims;
using Titan.API.Services.Auth;

namespace Titan.API.Services.RateLimiting;

/// <summary>
/// Middleware that adds rate limit headers to HTTP responses. (copied from PoE)
/// Also handles rate limit enforcement for HTTP endpoints.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RateLimitService rateLimitService, ISessionService sessionService)
    {
        // Skip for SignalR websocket connections (handled by hub filter)
        if (context.WebSockets.IsWebSocketRequest || 
            context.Request.Headers.ContainsKey("Upgrade"))
        {
            await _next(context);
            return;
        }

        try
        {
            // Skip if rate limiting disabled
            if (!await rateLimitService.IsEnabledAsync())
            {
                await _next(context);
                return;
            }

            var endpoint = context.Request.Path.Value ?? "/";
            // Get policy for this endpoint
            var policy = await rateLimitService.GetPolicyForEndpointAsync(endpoint);
            if (policy == null)
            {
                // All endpoints must have an explicit rate limit policy configured
                throw new InvalidOperationException(
                    $"No rate limit policy configured for endpoint: {endpoint}. " +
                    "Add a mapping in rate limiting configuration or use the admin API.");
            }

            // Get partition key: user ID for authenticated, IP for anonymous
            // First check if already authenticated (avoids duplicate session validation)
            string? userId = context.User?.Identity?.IsAuthenticated == true 
                ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) 
                : null;
            
            // Fall back to session validation for unauthenticated requests with tokens
            if (string.IsNullOrEmpty(userId))
            {
                userId = await ExtractUserIdFromSessionAsync(context, sessionService);
            }
            var partitionKey = !string.IsNullOrEmpty(userId)
                ? $"user:{userId}"
                : $"ip:{context.Connection.RemoteIpAddress}";
            var headerPrefix = !string.IsNullOrEmpty(userId) ? "Account" : "Ip";

            // Check rate limit
            var result = await rateLimitService.CheckAsync(partitionKey, policy.Name);

            // Add rate limit headers to response
            // X-Rate-Limit-Rules tells clients which rule types to look for
            context.Response.Headers["X-Rate-Limit-Rules"] = headerPrefix.ToLowerInvariant();
            context.Response.Headers["X-Rate-Limit-Policy"] = policy.Name;
            context.Response.Headers[$"X-Rate-Limit-{headerPrefix}"] = policy.ToHeaderValue();
            context.Response.Headers[$"X-Rate-Limit-{headerPrefix}-State"] = result.GetStateHeaderValue();

            if (!result.IsAllowed)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for {PartitionKey} on {Endpoint}. Policy: {Policy}, RetryAfter: {RetryAfter}s",
                    partitionKey, endpoint, policy.Name, result.RetryAfterSeconds);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = result.RetryAfterSeconds?.ToString() ?? "60";
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    retryAfter = result.RetryAfterSeconds,
                    policy = policy.Name
                });
                return;
            }
        }
        catch (Exception ex)
        {
            // Rate limiting failures fail hard - no silent pass-through
            // This catches: no policy configured, Redis down, Orleans unavailable, etc.
            _logger.LogError(ex, "Rate limiting failed for {Endpoint}", context.Request.Path);
            throw;
        }

        await _next(context);
    }

    /// <summary>
    /// Extracts the user ID from a session ticket stored in Redis.
    /// Checks both Authorization header and query string (for SignalR connections).
    /// </summary>
    private async Task<string?> ExtractUserIdFromSessionAsync(HttpContext context, ISessionService sessionService)
    {
        try
        {
            string? token = null;
            
            // First try Authorization header
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader["Bearer ".Length..].Trim();
            }
            
            // If no header, check query string (SignalR passes token this way)
            if (string.IsNullOrEmpty(token))
            {
                token = context.Request.Query["access_token"].FirstOrDefault();
            }
            
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            // Validate session and get user ID
            // ValidateSessionAsync also handles sliding expiration
            var session = await sessionService.ValidateSessionAsync(token);
            if (session == null)
            {
                return null;
            }

            return session.UserId.ToString();
        }
        catch
        {
            // If session validation fails for any reason, fall back to IP-based limiting
            return null;
        }
    }
}
