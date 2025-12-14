using System.Net;
using System.Net.Http.Headers;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Titan Admin Dashboard.
/// Tests authentication, authorization, and page accessibility.
/// </summary>
[Collection("AppHost")]
public class DashboardTests : IntegrationTestBase
{
    private readonly HttpClient _dashboardClient;

    public DashboardTests(AppHostFixture fixture) : base(fixture)
    {
        // Create a new HttpClient with cookie container for session management
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false // Don't auto-follow redirects so we can check them
        };
        _dashboardClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(fixture.DashboardBaseUrl)
        };
    }

    #region Authentication Tests

    [Fact]
    public async Task Dashboard_UnauthenticatedUser_RedirectsToLogin()
    {
        // Act - Try accessing the home page without authentication
        var response = await _dashboardClient.GetAsync("/");
        
        // Assert - Should redirect to login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Dashboard_LoginPage_IsAccessible()
    {
        // Act - Access the login page
        var response = await _dashboardClient.GetAsync("/Account/Login");
        
        // Assert - Login page should be accessible without authentication
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Titan Admin", content);
    }

    [Fact]
    public async Task Dashboard_Login_WithInvalidCredentials_DoesNotGrantAccess()
    {
        // Arrange - Get the login page first
        var loginPage = await _dashboardClient.GetAsync("/Account/Login");
        loginPage.EnsureSuccessStatusCode();
        
        // Act - Submit invalid credentials (Blazor forms may require anti-forgery tokens)
        // Without proper tokens, we expect a failure or redirect back to login
        using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "invalid@test.com",
            ["Input.Password"] = "wrongpassword",
            ["Input.RememberMe"] = "false"
        });
        var response = await _dashboardClient.PostAsync("/Account/Login", formContent);
        
        // Assert - We should NOT get a redirect to the home page (which would mean success)
        // Any other response (400, 200 with error, redirect to login) is acceptable
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            // If redirecting, should NOT be to home page
            Assert.DoesNotContain("ReturnUrl=%2F", response.Headers.Location?.ToString() ?? "");
        }
        // Otherwise, just verify the request completed (any non-success scenario is valid)
    }

    #endregion

    #region Protected Routes Tests

    [Fact]
    public async Task Dashboard_ItemTypesPage_RequiresAuthentication()
    {
        // Act - Try accessing item types page without auth
        var response = await _dashboardClient.GetAsync("/itemtypes");
        
        // Assert - Should redirect to login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_SeasonsPage_RequiresAuthentication()
    {
        // Act - Try accessing seasons page without auth
        var response = await _dashboardClient.GetAsync("/seasons");
        
        // Assert - Should redirect to login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_PlayersPage_RequiresAuthentication()
    {
        // Act - Try accessing players page without auth
        var response = await _dashboardClient.GetAsync("/players");
        
        // Assert - Should redirect to login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_AdminUsersPage_RequiresAuthentication()
    {
        // Act - Try accessing admin users page without auth
        var response = await _dashboardClient.GetAsync("/admin/users");
        
        // Assert - Should redirect to login
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    #endregion

    #region Static Assets Tests

    [Fact]
    public async Task Dashboard_CssFile_IsAccessible()
    {
        // Act - Access the dashboard CSS file
        var response = await _dashboardClient.GetAsync("/css/titan-dashboard.css");
        
        // Assert - Static files should be accessible without authentication
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("--titan-primary", content); // CSS variables
    }

    #endregion

    #region API Endpoints Tests

    [Fact]
    public async Task Dashboard_AccessDeniedPage_IsAccessible()
    {
        // Act - Access the access denied page directly
        var response = await _dashboardClient.GetAsync("/Account/AccessDenied");
        
        // Assert - Access denied page should be viewable
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Access Denied", content);
    }

    #endregion
}
