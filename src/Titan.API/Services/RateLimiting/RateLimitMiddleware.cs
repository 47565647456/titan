using System.IdentityModel.Tokens.Jwt;

namespace Titan.API.Services.RateLimiting;

/// <summary>
/// Middleware that adds rate limit headers to HTTP responses. (copied from PoE)
/// Also handles rate limit enforcement for HTTP endpoints.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RateLimitService rateLimitService)
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
                await _next(context);
                return;
            }

            // Get partition key: user ID for authenticated (from JWT), IP for anonymous
            // We extract the user ID directly from the JWT token since this middleware
            // runs before UseAuthentication() in the pipeline.
            var userId = ExtractUserIdFromToken(context);
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
            // If rate limiting fails (Redis/Orleans unavailable), allow the request through
            // This ensures the application remains functional during startup or infrastructure issues
            _logger.LogWarning(ex, "Rate limiting check failed, allowing request through");
        }

        await _next(context);
    }

    /// <summary>
    /// Extracts the user ID from a JWT Bearer token without full validation.
    /// This allows rate limiting by account before authentication middleware runs.
    /// Checks both Authorization header and query string (for SignalR connections).
    /// </summary>
    private string? ExtractUserIdFromToken(HttpContext context)
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

            // Read the token without validation - we just need the user ID for partitioning
            // The actual authentication middleware will validate the token later
            var jwt = _tokenHandler.ReadJwtToken(token);
            
            // Try to get the NameIdentifier claim (standard for user ID)
            var userId = jwt.Claims.FirstOrDefault(c => 
                c.Type == System.Security.Claims.ClaimTypes.NameIdentifier ||
                c.Type == "sub")?.Value;

            return userId;
        }
        catch
        {
            // If token parsing fails for any reason, fall back to IP-based limiting
            return null;
        }
    }
}
