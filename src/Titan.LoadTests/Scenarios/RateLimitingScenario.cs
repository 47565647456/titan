using System.Net;
using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;
using Titan.LoadTests.Infrastructure;

namespace Titan.LoadTests.Scenarios;

/// <summary>
/// Rate limiting stress test scenario.
/// Tests that the rate limiting system correctly enforces limits and returns 429s.
/// </summary>
public static class RateLimitingScenario
{
    private static readonly HttpClient SharedHttpClient;
    
    static RateLimitingScenario()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 1000,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        SharedHttpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
    
    /// <summary>
    /// Creates a scenario that tests rate limiting by hammering the auth endpoint.
    /// Tracks successful requests, rate-limited requests (429s), and errors separately.
    /// </summary>
    public static ScenarioProps Create(string baseUrl, int rate, TimeSpan duration)
    {
        return Scenario.Create("rate_limit_test", async context =>
        {
            // Use a single mock user to hit rate limits faster
            var mockToken = $"mock:ratelimit-test-user";
            var request = new { token = mockToken, provider = "Mock" };
            
            try
            {
                var response = await SharedHttpClient.PostAsJsonAsync($"{baseUrl}/api/auth/login", request);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Rate limited as expected - this is successful behavior for this test
                    var retryAfter = 0;
                    if (response.Headers.TryGetValues("Retry-After", out var values))
                    {
                        int.TryParse(values.FirstOrDefault(), out retryAfter);
                    }
                    
                    // Return special "rate_limited" status
                    return Response.Ok(
                        statusCode: "429",
                        message: $"Rate limited, retry after {retryAfter}s",
                        sizeBytes: 64);
                }
                
                if (response.IsSuccessStatusCode)
                {
                    return Response.Ok(
                        statusCode: "200",
                        sizeBytes: 256);
                }
                
                return Response.Fail(
                    statusCode: ((int)response.StatusCode).ToString(),
                    message: response.ReasonPhrase);
            }
            catch (Exception ex)
            {
                return Response.Fail(message: ex.Message);
            }
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // High rate to trigger rate limits quickly
            Simulation.Inject(rate: rate, interval: TimeSpan.FromSeconds(1), during: duration)
        );
    }
    
    /// <summary>
    /// Creates a scenario that tests graceful handling of rate limits with backoff.
    /// When rate limited, waits for retry-after before continuing.
    /// </summary>
    public static ScenarioProps CreateWithBackoff(string baseUrl, int rate, TimeSpan duration)
    {
        return Scenario.Create("rate_limit_with_backoff", async context =>
        {
            await using var client = new TitanClient(baseUrl);
            
            var result = await client.LoginWithRateLimitInfoAsync();
            
            if (result.RateLimited)
            {
                // Wait for retry-after period
                var waitTime = result.RetryAfterSeconds ?? 5;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(waitTime, 10))); // Cap at 10s
                
                // Retry once
                result = await client.LoginWithRateLimitInfoAsync();
            }
            
            return result.Success 
                ? Response.Ok(sizeBytes: 256)
                : result.RateLimited
                    ? Response.Ok(statusCode: "429", message: "Still rate limited after backoff", sizeBytes: 64)
                    : Response.Fail();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: rate, interval: TimeSpan.FromSeconds(1), during: duration)
        );
    }
}
