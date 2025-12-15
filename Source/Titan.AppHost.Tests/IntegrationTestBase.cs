using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;

namespace Titan.AppHost.Tests;

/// <summary>
/// Shared fixture that starts ONE AppHost and shares it across all tests.
/// This avoids port conflicts and speeds up test execution.
/// Uses DistributedApplicationTestingBuilder for Aspire test support.
/// </summary>
public class AppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public DistributedApplication App { get; private set; } = null!;
    public string ApiBaseUrl { get; private set; } = null!;
    public string DashboardBaseUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Create the AppHost using DistributedApplicationTestingBuilder with configuration as arguments
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Titan_AppHost>(
            [
                "--environment=Development",
                "Database:Volume=ephemeral",
                "Parameters:postgres-password=TestPassword123!"
            ]);
        
        // Add resilience to HTTP clients
        appHost.Services.ConfigureHttpClientDefaults(http => 
            http.AddStandardResilienceHandler());
        
        // Build and start the application
        App = await appHost.BuildAsync();
        await App.StartAsync();
            
        // Wait for ALL Orleans silo hosts to be healthy
        await Task.WhenAll(
            App.ResourceNotifications.WaitForResourceHealthyAsync("identity-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("inventory-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("trading-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("api"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("dashboard")
        ).WaitAsync(DefaultTimeout);
        
        // Give Orleans cluster time to stabilize
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Get API endpoint
        var endpoint = App.GetEndpoint("api", "https");
        ApiBaseUrl = endpoint.ToString().TrimEnd('/');
        
        // Get Dashboard endpoint (uses http since Dashboard might not have https configured)
        var dashboardEndpoint = App.GetEndpoint("dashboard", "http");
        DashboardBaseUrl = dashboardEndpoint.ToString().TrimEnd('/');
    }

    public async Task DisposeAsync()
    {
        if (App != null)
        {
            await App.DisposeAsync();
        }
    }
}


/// <summary>
/// Collection definition that uses the shared AppHost fixture.
/// All test classes that use [Collection("AppHost")] will share ONE instance.
/// </summary>
[CollectionDefinition("AppHost")]
public class AppHostCollection : ICollectionFixture<AppHostFixture>
{
    // This class has no code - it just links the collection name to the fixture
}

/// <summary>
/// Base class for integration tests that use the shared AppHost.
/// </summary>
public abstract class IntegrationTestBase
{
    protected readonly AppHostFixture Fixture;
    protected DistributedApplication App => Fixture.App;
    protected string ApiBaseUrl => Fixture.ApiBaseUrl;

    private HttpClient? _httpClient;
    protected HttpClient HttpClient => _httpClient ??= new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };

    protected IntegrationTestBase(AppHostFixture fixture)
    {
        Fixture = fixture;
    }

    #region Authentication Helpers

    /// <summary>
    /// Login via HTTP API and return the access token, refresh token, and expiry info.
    /// Uses the new /api/auth/login endpoint.
    /// </summary>
    protected async Task<(string AccessToken, string RefreshToken, int ExpiresInSeconds, Guid UserId)> LoginAsync(string mockToken, string provider = "Mock")
    {
        var request = new { token = mockToken, provider };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Failed to parse login response");
        
        if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
            throw new InvalidOperationException($"Login failed");
        
        return (result.AccessToken, result.RefreshToken!, result.AccessTokenExpiresInSeconds!.Value, result.UserId!.Value);
    }

    protected async Task<(string AccessToken, string RefreshToken, int ExpiresInSeconds, Guid UserId)> LoginAsUserAsync()
        => await LoginAsync($"mock:{Guid.NewGuid()}");

    protected async Task<(string AccessToken, string RefreshToken, int ExpiresInSeconds, Guid UserId)> LoginAsAdminAsync()
        => await LoginAsync($"mock:admin:{Guid.NewGuid()}");

    protected HubConnection CreateHubConnection(string hubPath, string token)
        => new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}{hubPath}?access_token={token}")
            .Build();

    protected async Task<HubConnection> ConnectToHubAsync(string hubPath, string token)
    {
        var hub = CreateHubConnection(hubPath, token);
        await hub.StartAsync();
        return hub;
    }

    /// <summary>
    /// Ensures a test base type exists in the registry. Requires admin token.
    /// </summary>
    protected async Task EnsureBaseTypeExistsAsync(string adminToken, string baseTypeId, bool isTradeable = true)
    {
        var hub = await ConnectToHubAsync("/baseTypeHub", adminToken);
        try
        {
            // Try to get it first
            var existing = await hub.InvokeAsync<BaseType?>("Get", baseTypeId);
            if (existing != null) return;
        }
        catch
        {
            // Base type doesn't exist, create it
        }
        
        var baseType = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = baseTypeId.Replace("_", " "),
            Category = ItemCategory.Currency,
            Slot = EquipmentSlot.None,
            Width = 1,
            Height = 1,
            MaxStackSize = 20,
            IsTradeable = isTradeable
        };
        
        await hub.InvokeAsync<BaseType>("Create", baseType);
        await hub.DisposeAsync();
    }

    /// <summary>
    /// Ensures a test base type exists in the registry using a UserSession.
    /// </summary>
    protected async Task EnsureBaseTypeExistsAsync(UserSession adminSession, string baseTypeId, bool isTradeable = true)
    {
        var hub = await adminSession.GetBaseTypeHubAsync();
        try
        {
            // Try to get it first
            var existing = await hub.InvokeAsync<BaseType?>("Get", baseTypeId);
            if (existing != null) return;
        }
        catch
        {
            // Base type doesn't exist, create it
        }
        
        var baseType = new BaseType
        {
            BaseTypeId = baseTypeId,
            Name = baseTypeId.Replace("_", " "),
            Category = ItemCategory.Currency,
            Slot = EquipmentSlot.None,
            Width = 1,
            Height = 1,
            MaxStackSize = 20,
            IsTradeable = isTradeable
        };
        
        await hub.InvokeAsync<BaseType>("Create", baseType);
    }

    #endregion

    #region UserSession Factory

    /// <summary>
    /// Creates a new user session with a fresh user account.
    /// The session manages hub connections efficiently, reusing them across calls.
    /// </summary>
    protected async Task<UserSession> CreateUserSessionAsync()
    {
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();
        return new UserSession(ApiBaseUrl, accessToken, refreshToken, expiresIn, userId);
    }

    /// <summary>
    /// Creates a new admin session with admin privileges.
    /// The session manages hub connections efficiently, reusing them across calls.
    /// </summary>
    protected async Task<UserSession> CreateAdminSessionAsync()
    {
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsAdminAsync();
        return new UserSession(ApiBaseUrl, accessToken, refreshToken, expiresIn, userId);
    }

    #endregion
}
