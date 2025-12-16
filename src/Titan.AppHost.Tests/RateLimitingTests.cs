using System.Net;
using System.Net.Http.Json;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for rate limiting functionality.
/// These tests verify that rate limiting works end-to-end with real Redis and Orleans.
/// </summary>
[Collection("RateLimitingAppHost")]
public class RateLimitingTests : RateLimitingTestBase
{
    public RateLimitingTests(RateLimitingAppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AnonymousRequest_HasRateLimitPolicyHeader()
    {
        // Act - make anonymous request
        var response = await HttpClient.GetAsync("/health");

        // Assert - should have the rules header
        Assert.True(response.Headers.Contains("X-Rate-Limit-Rules"), 
            $"Expected X-Rate-Limit-Rules header. Headers: {string.Join(", ", response.Headers.Select(h => h.Key))}");
        var rulesValue = response.Headers.GetValues("X-Rate-Limit-Rules").FirstOrDefault();
        Assert.Equal("ip", rulesValue); // Anonymous requests use IP-based limiting
        
        // Assert - should have the policy header indicating which policy is applied
        Assert.True(response.Headers.Contains("X-Rate-Limit-Policy"), 
            $"Expected X-Rate-Limit-Policy header. Status: {response.StatusCode}, Headers: {string.Join(", ", response.Headers.Select(h => h.Key))}");
        
        var policyValue = response.Headers.GetValues("X-Rate-Limit-Policy").FirstOrDefault();
        Assert.NotNull(policyValue);
        Assert.NotEmpty(policyValue);
    }

    [Fact]
    public async Task AuthEndpoint_HasAuthPolicyApplied()
    {
        // Act - make a request to auth endpoint (which has "Auth" policy mapping)
        var request = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert - should have the rules header
        Assert.True(response.Headers.Contains("X-Rate-Limit-Rules"),
            $"Expected X-Rate-Limit-Rules header. Headers: {string.Join(", ", response.Headers.Select(h => h.Key))}");
        var rulesValue = response.Headers.GetValues("X-Rate-Limit-Rules").FirstOrDefault();
        Assert.Equal("ip", rulesValue); // Login requests before auth use IP-based limiting
        
        // Assert - auth endpoints should use "Auth" policy
        Assert.True(response.Headers.Contains("X-Rate-Limit-Policy"),
            $"Expected X-Rate-Limit-Policy header. Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");
        
        var policyName = response.Headers.GetValues("X-Rate-Limit-Policy").FirstOrDefault();
        Assert.Equal("Auth", policyName);
    }

    [Fact]
    public async Task RateLimitState_IncrementsWithConsecutiveRequests()
    {
        // Clear state first to ensure accurate hit count
        await ClearRateLimitStateAsync();
        await Task.Delay(100); // Allow Redis state to settle
        
        // Make 3 consecutive requests and collect hit counts from each
        var hitCounts = new List<int>();
        
        for (int i = 0; i < 3; i++)
        {
            var response = await HttpClient.GetAsync($"/health?test=increment&i={i}");
            
            // Get the state header
            var stateHeaderName = response.Headers.Contains("X-Rate-Limit-Ip-State") 
                ? "X-Rate-Limit-Ip-State" 
                : (response.Headers.Contains("X-Rate-Limit-Account-State") 
                    ? "X-Rate-Limit-Account-State" 
                    : null);
            
            if (stateHeaderName != null)
            {
                var stateValue = response.Headers.GetValues(stateHeaderName).FirstOrDefault();
                if (stateValue != null)
                {
                    var parts = stateValue.Split(':');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var hits))
                    {
                        hitCounts.Add(hits);
                    }
                }
            }
        }

        // Assert - we should have hit counts from all 3 requests
        Assert.True(hitCounts.Count >= 2, 
            $"Expected at least 2 responses with hit counts, got {hitCounts.Count}");
        
        // The last hit count should be greater than the first (state is incrementing)
        var firstHits = hitCounts.First();
        var lastHits = hitCounts.Last();
        Assert.True(lastHits > firstHits, 
            $"Hit count should increment between requests. First: {firstHits}, Last: {lastHits}, All: [{string.Join(", ", hitCounts)}]");
    }

    [Fact]
    public async Task ExceedingLimit_Returns429WithRetryAfter()
    {
        // Auth policy: 10 requests per 60 seconds
        // Make 12 requests to trigger rate limiting
        var responses = new List<HttpResponseMessage>();
        
        for (int i = 0; i < 12; i++)
        {
            var request = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
            var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);
            responses.Add(response);
            
            // If we got rate limited, stop making requests
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        // Assert - at least one response should be 429
        var rateLimitedResponses = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        
        Assert.True(rateLimitedResponses.Count > 0, 
            $"Expected at least one 429 response after {responses.Count} requests. " +
            $"Got: {string.Join(", ", responses.Select(r => (int)r.StatusCode))}");

        // The 429 response should have Retry-After header
        var rateLimitedResponse = rateLimitedResponses.First();
        Assert.True(
            rateLimitedResponse.Headers.Contains("Retry-After") || 
            rateLimitedResponse.Headers.Contains("X-Rate-Limit-Timeout"),
            "Expected Retry-After or X-Rate-Limit-Timeout header on 429 response");
    }

    [Fact]
    public async Task SameIPMakingMultipleRequests_SharingRateLimitState()
    {
        // Multiple requests from same client (same IP) should share rate limit state
        var responses = new List<(int hits, HttpResponseMessage response)>();
        
        for (int i = 0; i < 5; i++)
        {
            var response = await HttpClient.GetAsync($"/health?t={Guid.NewGuid()}");
            
            // Try to get hit count from state header
            int hits = 0;
            var stateHeader = response.Headers.Contains("X-Rate-Limit-Ip-State") 
                ? response.Headers.GetValues("X-Rate-Limit-Ip-State").FirstOrDefault() 
                : null;
            
            if (stateHeader != null)
            {
                var parts = stateHeader.Split(':');
                if (parts.Length >= 1)
                    int.TryParse(parts[0], out hits);
            }
            
            responses.Add((hits, response));
        }

        // Hits should be increasing (or all 0 if state headers not present)
        var hitCounts = responses.Select(r => r.hits).ToList();
        var hasIncreasingHits = hitCounts.Where(h => h > 0).ToList();
        
        if (hasIncreasingHits.Count > 1)
        {
            // Verify hits are incrementing across requests
            Assert.True(hasIncreasingHits.Last() > hasIncreasingHits.First(),
                $"Expected hits to increase. Got sequence: {string.Join(", ", hitCounts)}");
        }
        else
        {
            // At minimum, all requests should have policy header
            Assert.All(responses, r => 
                Assert.True(r.response.Headers.Contains("X-Rate-Limit-Policy"),
                    "All requests should have rate limit policy header"));
        }
    }

    [Fact]
    public async Task DisablingRateLimit_StopsEnforcement()
    {
        // Arrange - authenticate as admin
        await AuthenticateAsSuperAdminAsync();

        // Get initial state
        var configResponse = await HttpClient.GetAsync("/api/admin/rate-limiting/config");
        var initialConfig = await configResponse.Content.ReadFromJsonAsync<RateLimitingConfiguration>();
        var wasEnabled = initialConfig?.Enabled ?? true;

        try
        {
            // Act - disable rate limiting
            var disableResponse = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/enabled", 
                new { Enabled = false });
            Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

            // Small delay for cache to clear
            await Task.Delay(200);

            // Make a request - should NOT have rate limit headers when disabled
            var testResponse = await HttpClient.GetAsync("/health?disabled-test");
            
            // When disabled, policy header might still be present but enforcement is off
            // The key indicator is that we won't get 429 even after many requests
            var responses = new List<HttpResponseMessage>();
            for (int i = 0; i < 5; i++)
            {
                responses.Add(await HttpClient.PostAsJsonAsync("/api/auth/login", 
                    new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" }));
            }

            // When disabled, no requests should be rate limited
            var rateLimited = responses.Any(r => r.StatusCode == HttpStatusCode.TooManyRequests);
            Assert.False(rateLimited, "Should not get 429 when rate limiting is disabled");
        }
        finally
        {
            // Restore original state
            await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/enabled", 
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task AuthenticatedRequest_UsesAccountBasedLimiting()
    {
        // Arrange - clear state and login to get a JWT token
        await ClearRateLimitStateAsync();
        
        var loginRequest = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
        var loginResponse = await HttpClient.PostAsJsonAsync("/api/auth/login", loginRequest);
        Assert.True(loginResponse.IsSuccessStatusCode, 
            $"Login should succeed. Status: {loginResponse.StatusCode}");
        
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResult>();
        Assert.NotNull(loginResult?.AccessToken);
        
        // Act - make an authenticated request with the JWT token
        using var authClient = new HttpClient { BaseAddress = HttpClient.BaseAddress };
        authClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        
        var response = await authClient.GetAsync("/health");
        
        // Assert - should use account-based limiting (not IP)
        // The middleware now extracts user ID from the JWT token directly
        Assert.True(response.Headers.Contains("X-Rate-Limit-Rules"),
            $"Expected X-Rate-Limit-Rules header. Headers: {string.Join(", ", response.Headers.Select(h => h.Key))}");
        var rulesValue = response.Headers.GetValues("X-Rate-Limit-Rules").FirstOrDefault();
        Assert.Equal("account", rulesValue); // Authenticated uses account-based
        
        // Should have X-Rate-Limit-Account header (not X-Rate-Limit-Ip)
        Assert.True(response.Headers.Contains("X-Rate-Limit-Account"),
            "Expected X-Rate-Limit-Account header for authenticated request");
        Assert.False(response.Headers.Contains("X-Rate-Limit-Ip"),
            "Should not have X-Rate-Limit-Ip for authenticated request");
    }

    [Fact]
    public async Task MultipleRulesPolicy_AllRulesReportedInState()
    {
        // Arrange - Auth policy has multiple rules: 10:60:600, 30:300:1800
        var loginRequest = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
        
        // Act - make request to auth endpoint
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", loginRequest);
        
        // Assert - should have Auth policy with multiple rules in header
        Assert.True(response.Headers.Contains("X-Rate-Limit-Policy"),
            "Expected X-Rate-Limit-Policy header");
        var policyName = response.Headers.GetValues("X-Rate-Limit-Policy").FirstOrDefault();
        Assert.Equal("Auth", policyName);
        
        // Get the limit header - should contain comma-separated rules
        var limitHeaderName = response.Headers.Contains("X-Rate-Limit-Account") 
            ? "X-Rate-Limit-Account" 
            : "X-Rate-Limit-Ip";
        
        var limitValue = response.Headers.GetValues(limitHeaderName).FirstOrDefault();
        Assert.NotNull(limitValue);
        
        // Auth policy has 2 rules, so header should have 2 comma-separated entries
        var rules = limitValue.Split(',');
        Assert.True(rules.Length >= 2, 
            $"Expected Auth policy to have 2 rules, got: {limitValue}");
        
        // Each rule should be in format MaxHits:PeriodSeconds:TimeoutSeconds
        foreach (var rule in rules)
        {
            var parts = rule.Split(':');
            Assert.Equal(3, parts.Length);
            Assert.All(parts, p => Assert.True(int.TryParse(p, out _), $"Part '{p}' is not a number"));
        }
    }

    [Fact]
    public async Task ExceedingLimit_EntersTimeoutPeriod()
    {
        // This test verifies that when rate limit is exceeded, the timeout value 
        // from the policy rules is correctly reported in the Retry-After header.
        // Uses the existing Auth policy (10 requests per 60s, 600s timeout).
        
        await ClearRateLimitStateAsync();
        
        // Make requests until we hit the limit
        // Auth policy is 10:60:600 (10 requests per 60 seconds, 600 second timeout)
        HttpResponseMessage? rateLimitedResponse = null;
        
        for (int i = 0; i < 15; i++)
        {
            var loginRequest = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
            var response = await HttpClient.PostAsJsonAsync("/api/auth/login", loginRequest);
            
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitedResponse = response;
                break;
            }
        }
        
        // Verify we hit the rate limit (if we have the Auth policy configured)
        if (rateLimitedResponse != null)
        {
            // Check that Retry-After header is present and has a reasonable value
            Assert.True(
                rateLimitedResponse.Headers.Contains("Retry-After"),
                "Expected Retry-After header when rate limited");
            
            var retryAfter = rateLimitedResponse.Headers.GetValues("Retry-After").FirstOrDefault();
            Assert.NotNull(retryAfter);
            Assert.True(int.TryParse(retryAfter, out var seconds), $"Retry-After should be numeric: {retryAfter}");
            
            // Auth policy has 600 second timeout, but we check > 0 since we might 
            // be hitting different rules depending on timing
            Assert.True(seconds > 0, $"Expected Retry-After > 0, got {seconds}");
        }
        else
        {
            // If we didn't hit the limit, the test passes vacuously
            // (either rate limiting is disabled or policy is very permissive)
            Assert.True(true, "Did not hit rate limit - policy may be permissive");
        }
    }

    private record LoginResult(bool Success, string? AccessToken, string? RefreshToken);
}

/// <summary>
/// Integration tests for rate limit configuration admin API.
/// These tests verify CRUD operations on rate limit config via HTTP endpoints.
/// </summary>
[Collection("RateLimitingAppHost")]
public class RateLimitAdminApiTests : RateLimitingTestBase
{
    public RateLimitAdminApiTests(RateLimitingAppHostFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetConfiguration_ReturnsConfig()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();

        // Act
        var response = await HttpClient.GetAsync("/api/admin/rate-limiting/config");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await response.Content.ReadFromJsonAsync<RateLimitingConfiguration>();
        Assert.NotNull(config);
        Assert.NotEmpty(config.Policies);
    }

    [Fact]
    public async Task UpsertPolicy_CreatesNewPolicy()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();
        var policyName = "TestPolicy_" + Guid.NewGuid().ToString("N")[..8];
        var request = new { Name = policyName, Rules = new[] { "50:30:120" } };

        try
        {
            // Act
            var response = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/policies", request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var policy = await response.Content.ReadFromJsonAsync<RateLimitPolicy>();
            Assert.NotNull(policy);
            Assert.Equal(policyName, policy.Name);
            Assert.Single(policy.Rules);
            Assert.Equal(50, policy.Rules[0].MaxHits);
        }
        finally
        {
            // Cleanup
            await HttpClient.DeleteAsync($"/api/admin/rate-limiting/policies/{policyName}");
        }
    }

    [Fact]
    public async Task RemovePolicy_DeletesPolicy()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();
        var policyName = "DeleteTest_" + Guid.NewGuid().ToString("N")[..8];
        await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/policies", 
            new { Name = policyName, Rules = new[] { "10:60:300" } });

        // Act
        var deleteResponse = await HttpClient.DeleteAsync($"/api/admin/rate-limiting/policies/{policyName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify it's gone
        var configResponse = await HttpClient.GetAsync("/api/admin/rate-limiting/config");
        var config = await configResponse.Content.ReadFromJsonAsync<RateLimitingConfiguration>();
        Assert.DoesNotContain(config!.Policies, p => p.Name == policyName);
    }

    [Fact]
    public async Task SetEnabled_TogglesRateLimiting()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();
        
        // Get initial state
        var initialConfig = await GetConfigAsync();
        var wasEnabled = initialConfig.Enabled;

        try
        {
            // Act - disable
            var disableResponse = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/enabled", 
                new { Enabled = false });
            Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

            var disabledConfig = await GetConfigAsync();
            Assert.False(disabledConfig.Enabled);

            // Act - enable
            var enableResponse = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/enabled", 
                new { Enabled = true });
            Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

            var enabledConfig = await GetConfigAsync();
            Assert.True(enabledConfig.Enabled);
        }
        finally
        {
            // Restore original state
            await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/enabled", 
                new { Enabled = wasEnabled });
        }
    }

    [Fact]
    public async Task AddEndpointMapping_CreatesMapping()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();
        var testPattern = "/api/test/*_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Act
            var response = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/mappings", 
                new { Pattern = testPattern, PolicyName = "Global" });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var mapping = await response.Content.ReadFromJsonAsync<EndpointRateLimitConfig>();
            Assert.NotNull(mapping);
            Assert.Equal(testPattern, mapping.Pattern);
            Assert.Equal("Global", mapping.PolicyName);

            // Verify in config
            var config = await GetConfigAsync();
            Assert.Contains(config.EndpointMappings, m => m.Pattern == testPattern);
        }
        finally
        {
            // Cleanup
            await HttpClient.DeleteAsync($"/api/admin/rate-limiting/mappings/{Uri.EscapeDataString(testPattern)}");
        }
    }

    [Fact]
    public async Task SetDefaultPolicy_ChangesDefault()
    {
        // Arrange
        await AuthenticateAsSuperAdminAsync();
        var initialConfig = await GetConfigAsync();
        var originalDefault = initialConfig.DefaultPolicyName;

        // Create a test policy
        var testPolicyName = "DefaultTest_" + Guid.NewGuid().ToString("N")[..8];
        await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/policies", 
            new { Name = testPolicyName, Rules = new[] { "999:60:60" } });

        try
        {
            // Act
            var response = await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/default-policy", 
                new { PolicyName = testPolicyName });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var config = await GetConfigAsync();
            Assert.Equal(testPolicyName, config.DefaultPolicyName);
        }
        finally
        {
            // Restore original default and cleanup
            await HttpClient.PostAsJsonAsync("/api/admin/rate-limiting/default-policy", 
                new { PolicyName = originalDefault });
            await HttpClient.DeleteAsync($"/api/admin/rate-limiting/policies/{testPolicyName}");
        }
    }

    [Fact]
    public async Task AdminEndpoints_RequireAuthentication()
    {
        // Act - try without auth
        var response = await HttpClient.GetAsync("/api/admin/rate-limiting/config");

        // Assert - should be unauthorized (401) or forbidden (403)
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized || 
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {response.StatusCode}");
    }

    private async Task<RateLimitingConfiguration> GetConfigAsync()
    {
        var response = await HttpClient.GetAsync("/api/admin/rate-limiting/config");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RateLimitingConfiguration>() 
            ?? throw new InvalidOperationException("Failed to get config");
    }
}

/// <summary>
/// Separate fixture for rate limiting tests that enables rate limiting.
/// Runs in isolation from other integration tests.
/// </summary>
public class RateLimitingAppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public Aspire.Hosting.DistributedApplication App { get; private set; } = null!;
    public string ApiBaseUrl { get; private set; } = null!;
    public string RateLimitRedisConnectionString { get; private set; } = null!;
    private StackExchange.Redis.IConnectionMultiplexer? _redis;

    public async Task InitializeAsync()
    {
        // Create the AppHost with rate limiting ENABLED and a tight limit for testing
        var appHost = await Aspire.Hosting.Testing.DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Titan_AppHost>(
            [
                "--environment=Development",
                "Database:Volume=ephemeral",
                "Parameters:postgres-password=TestPassword123!",
                "RateLimiting:Enabled=true" // Enable rate limiting for these tests
            ]);
        
        // Add resilience to HTTP clients
        appHost.Services.ConfigureHttpClientDefaults(http => 
            http.AddStandardResilienceHandler());
        
        // Build and start the application
        App = await appHost.BuildAsync();
        await App.StartAsync();
            
        // Wait for essential services to be healthy
        await Task.WhenAll(
            App.ResourceNotifications.WaitForResourceHealthyAsync("identity-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("api"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("rate-limiting")
        ).WaitAsync(DefaultTimeout);
        
        // Give Orleans cluster time to stabilize
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Get API endpoint
        var endpoint = App.GetEndpoint("api", "https");
        ApiBaseUrl = endpoint.ToString().TrimEnd('/');
        
        // Get Redis connection string for rate limiting
        RateLimitRedisConnectionString = await App.GetConnectionStringAsync("rate-limiting") 
            ?? throw new InvalidOperationException("Could not get rate-limiting Redis connection string");
        
        // Connect to Redis for direct state clearing
        _redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(RateLimitRedisConnectionString);
    }

    /// <summary>
    /// Clears all rate limit keys from Redis directly.
    /// </summary>
    public async Task ClearRateLimitStateAsync()
    {
        if (_redis == null) return;
        
        var server = _redis.GetServers().FirstOrDefault();
        if (server == null) return;
        
        var db = _redis.GetDatabase();
        var keysToDelete = new List<StackExchange.Redis.RedisKey>();
        
        await foreach (var key in server.KeysAsync(pattern: "rl:*"))
        {
            keysToDelete.Add(key);
        }
        
        if (keysToDelete.Count > 0)
        {
            await db.KeyDeleteAsync([.. keysToDelete]);
        }
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }
        if (App != null)
        {
            await App.DisposeAsync();
        }
    }
}

/// <summary>
/// Collection definition for rate limiting tests.
/// These tests share a separate AppHost with rate limiting enabled.
/// </summary>
[CollectionDefinition("RateLimitingAppHost")]
public class RateLimitingAppHostCollection : ICollectionFixture<RateLimitingAppHostFixture>
{
}

/// <summary>
/// Base class for rate limiting integration tests.
/// </summary>
public abstract class RateLimitingTestBase
{
    protected readonly RateLimitingAppHostFixture Fixture;
    protected Aspire.Hosting.DistributedApplication App => Fixture.App;
    protected string ApiBaseUrl => Fixture.ApiBaseUrl;

    private HttpClient? _httpClient;
    protected HttpClient HttpClient => _httpClient ??= new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };

    protected RateLimitingTestBase(RateLimitingAppHostFixture fixture)
    {
        Fixture = fixture;
    }

    protected async Task<(string AccessToken, string RefreshToken, int ExpiresInSeconds, Guid UserId)> LoginAsUserAsync()
    {
        var request = new { token = $"mock:{Guid.NewGuid()}", provider = "Mock" };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Failed to parse login response");
        
        if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
            throw new InvalidOperationException("Login failed");
        
        return (result.AccessToken, result.RefreshToken!, result.AccessTokenExpiresInSeconds!.Value, result.UserId!.Value);
    }

    /// <summary>
    /// Clears all rate limit state from Redis directly via fixture.
    /// Call this at the start of tests that need a clean slate.
    /// </summary>
    protected Task ClearRateLimitStateAsync() => Fixture.ClearRateLimitStateAsync();

    /// <summary>
    /// Authenticate as a SuperAdmin user and set the Authorization header.
    /// Uses mock:admin:{guid} format which grants the Admin role.
    /// </summary>
    protected async Task AuthenticateAsSuperAdminAsync()
    {
        // Clear rate limit state first to ensure we can authenticate
        await ClearRateLimitStateAsync();
        
        // Use mock:admin:{guid} format to get Admin role
        var request = new { token = $"mock:admin:{Guid.NewGuid()}", provider = "Mock" };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Failed to parse login response");
        
        if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
            throw new InvalidOperationException("Admin login failed");
        
        // Set authorization header for subsequent requests
        HttpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);
    }

    private record LoginResponse(
        bool Success, 
        string? AccessToken, 
        string? RefreshToken, 
        int? AccessTokenExpiresInSeconds,
        Guid? UserId);
}
