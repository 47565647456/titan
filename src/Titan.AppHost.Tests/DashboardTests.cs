using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Admin Dashboard API endpoints.
/// Tests session-based authentication and admin API accessibility.
/// </summary>
[Collection("AppHost")]
public class DashboardTests : IntegrationTestBase
{
    public DashboardTests(AppHostFixture fixture) : base(fixture)
    {
    }

    #region Authentication Tests

    [Fact]
    public async Task AdminAuth_UnauthenticatedRequest_Returns401()
    {
        // Act - Try accessing admin endpoint without authentication
        var response = await HttpClient.GetAsync("/api/admin/accounts");
        
        // Assert - Should return 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminAuth_InvalidCredentials_Returns401()
    {
        // Act - Try logging in with invalid credentials
        var response = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "invalid@example.com",
            password = "wrongpassword"
        });
        
        // Assert - Should return 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminAuth_ValidCredentials_ReturnsSession()
    {
        // Act - Login with valid admin credentials
        var response = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        
        // Assert - Should return success with session
        response.EnsureSuccessStatusCode();
        var loginResponse = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        
        Assert.NotNull(loginResponse);
        Assert.True(loginResponse.Success);
        Assert.False(string.IsNullOrEmpty(loginResponse.SessionId));
        Assert.NotEmpty(loginResponse.Roles);
    }

    [Fact]
    public async Task AdminAuth_WithSession_CanAccessProtectedEndpoints()
    {
        // Arrange - Login first
        var loginResponse = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        
        // Create authenticated client
        var authClient = new HttpClient { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        authClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", login!.SessionId);
        
        // Act - Access protected endpoint
        var response = await authClient.GetAsync("/api/admin/auth/me");
        
        // Assert - Should succeed
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminAuth_Logout_ReturnsSuccess()
    {
        // Arrange - Login first
        var loginResponse = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        
        // Create authenticated client
        var authClient = new HttpClient { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        authClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", login!.SessionId);
        
        // Act - Logout
        var logoutResponse = await authClient.PostAsync("/api/admin/auth/logout", null);
        
        // Assert - Should succeed
        logoutResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminAuth_Logout_WithoutAuth_Returns401()
    {
        // Act - Try to logout without being authenticated
        var response = await HttpClient.PostAsync("/api/admin/auth/logout", null);
        
        // Assert - Should return 401
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminAuth_Login_SetsHttpOnlyCookies()
    {
        // Act - Login with valid credentials
        var response = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        
        // Assert - Should set httpOnly cookies
        response.EnsureSuccessStatusCode();
        
        Assert.True(response.Headers.Contains("Set-Cookie"), "Expected Set-Cookie header");
        var cookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
        
        // Should have session cookie
        Assert.True(cookieHeaders.Any(c => c.Contains("admin_session")), 
            "Expected admin_session cookie");
        
        // Session cookie should be httpOnly
        Assert.All(cookieHeaders.Where(c => c.Contains("admin_")), 
            cookie => Assert.Contains("httponly", cookie.ToLowerInvariant()));
    }

    [Fact]
    public async Task AdminAuth_Logout_InvalidatesSession()
    {
        // Arrange - Login to get session
        using var handler = new HttpClientHandler { UseCookies = true };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        
        var loginResponse = await client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        
        // Set auth header
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", loginResult!.SessionId);
        
        // Logout
        var logoutResponse = await client.PostAsync("/api/admin/auth/logout", null);
        logoutResponse.EnsureSuccessStatusCode();
        
        // Act - Try to access protected endpoint with the invalidated session
        var response = await client.GetAsync("/api/admin/auth/me");
        
        // Assert - Should fail because session is invalidated
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminAuth_RevokeAll_InvalidatesAllSessions()
    {
        // Arrange - Login twice to simulate multiple sessions
        using var handler1 = new HttpClientHandler { UseCookies = true };
        using var client1 = new HttpClient(handler1) { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        
        var login1 = await client1.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        login1.EnsureSuccessStatusCode();
        var loginResult1 = await login1.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client1.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", loginResult1!.SessionId);
        
        // Second "session" 
        using var handler2 = new HttpClientHandler { UseCookies = true };
        using var client2 = new HttpClient(handler2) { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        
        var login2 = await client2.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        login2.EnsureSuccessStatusCode();
        var loginResult2 = await login2.Content.ReadFromJsonAsync<AdminLoginResponse>();
        client2.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", loginResult2!.SessionId);
        
        // Act - Revoke all sessions from first session
        var revokeResponse = await client1.PostAsync("/api/admin/auth/revoke-all", null);
        revokeResponse.EnsureSuccessStatusCode();
        
        // Assert - Second session should be invalidated
        var response = await client2.GetAsync("/api/admin/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Admin Endpoints Tests

    [Fact]
    public async Task AdminAccounts_WithAuth_ReturnsAccountList()
    {
        // Arrange - Get auth token
        using var authClient = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Get accounts
        var response = await authClient.GetAsync("/api/admin/accounts");
        
        // Assert - Should return success (even if empty list)
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminSeasons_WithAuth_ReturnsSeasonList()
    {
        // Arrange - Get auth token
        using var authClient = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Get seasons
        var response = await authClient.GetAsync("/api/admin/seasons");
        
        // Assert - Should return success
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminBaseTypes_WithAuth_ReturnsBaseTypeList()
    {
        // Arrange - Get auth token
        using var authClient = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Get base types
        var response = await authClient.GetAsync("/api/admin/base-types");
        
        // Assert - Should return success
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminRateLimiting_WithAuth_ReturnsConfig()
    {
        // Arrange - Get auth token
        using var authClient = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Get rate limiting config
        var response = await authClient.GetAsync("/api/admin/rate-limiting/config");
        
        // Assert - Should return success
        response.EnsureSuccessStatusCode();
    }

    #endregion

    #region Helpers

    private async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        var loginResponse = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
        {
            email = "admin@titan.local",
            password = "Admin123!"
        });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AdminLoginResponse>();
        
        var client = new HttpClient { BaseAddress = new Uri(Fixture.ApiBaseUrl) };
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", login!.SessionId);
        return client;
    }

    private record AdminLoginResponse(
        bool Success,
        Guid UserId,
        string Email,
        string? DisplayName,
        List<string> Roles,
        string SessionId,
        DateTimeOffset ExpiresAt);

    #endregion
}
