using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;
using Titan.Abstractions.Models.Items;
using LoginResponse = Titan.Abstractions.Contracts.LoginResponse;

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
    private string? _adminDbConnectionString;

    public async Task InitializeAsync()
    {
        // Create the AppHost using DistributedApplicationTestingBuilder with configuration as arguments
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Titan_AppHost>(
            [
                "--environment=Development",
                "Database:Volume=ephemeral",
                "Parameters:postgres-password=TestPassword123!",
                "RateLimiting:Enabled=false" // Disable rate limiting for existing tests
            ]);
        
        // Add resilience to HTTP clients
        appHost.Services.ConfigureHttpClientDefaults(http => 
            http.AddStandardResilienceHandler());
        
        // Build and start the application
        App = await appHost.BuildAsync();
        await App.StartAsync();
            
        // Wait for ALL Orleans silo hosts and Redis to be healthy
        await Task.WhenAll(
            App.ResourceNotifications.WaitForResourceHealthyAsync("identity-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("inventory-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("trading-host"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("api"),
            App.ResourceNotifications.WaitForResourceHealthyAsync("rate-limiting"), // Rate-limiting storage Redis
            App.ResourceNotifications.WaitForResourceHealthyAsync("sessions")       // Session storage Redis
        ).WaitAsync(DefaultTimeout);
        
        // Give Orleans cluster time to stabilize
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Get API endpoint
        var endpoint = App.GetEndpoint("api", "https");
        ApiBaseUrl = endpoint.ToString().TrimEnd('/');
        
        // Get admin database connection string
        _adminDbConnectionString = await App.GetConnectionStringAsync("titan-admin");
    }

    /// <summary>
    /// Resets the lockout status for the admin user directly in the database.
    /// Call this before tests that need to authenticate as admin.
    /// </summary>
    public async Task ResetAdminLockoutAsync()
    {
        if (string.IsNullOrEmpty(_adminDbConnectionString))
            return;
        
        await using var connection = new NpgsqlConnection(_adminDbConnectionString);
        await connection.OpenAsync();
        
        // Reset lockout_end and access_failed_count for the admin user
        await using var command = new NpgsqlCommand(
            """
            UPDATE public.admin_users 
            SET lockout_end = NULL, access_failed_count = 0 
            WHERE normalized_email = 'ADMIN@TITAN.LOCAL'
            """, 
            connection);
        
        await command.ExecuteNonQueryAsync();
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
    /// Login via HTTP API and return the session ID, expiry, and user ID.
    /// Uses the new /api/auth/login endpoint.
    /// </summary>
    protected async Task<(string SessionId, DateTimeOffset ExpiresAt, Guid UserId)> LoginAsync(string mockToken, string provider = "Mock")
    {
        var request = new { token = mockToken, provider };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Failed to parse login response");
        
        if (!result.Success || string.IsNullOrEmpty(result.SessionId))
            throw new InvalidOperationException($"Login failed");
        
        return (result.SessionId, result.ExpiresAt!.Value, result.UserId!.Value);
    }

    protected async Task<(string SessionId, DateTimeOffset ExpiresAt, Guid UserId)> LoginAsUserAsync()
        => await LoginAsync($"mock:{Guid.NewGuid()}");

    protected async Task<(string SessionId, DateTimeOffset ExpiresAt, Guid UserId)> LoginAsAdminAsync()
        => await LoginAsync($"mock:admin:{Guid.NewGuid()}");

    protected HubConnection CreateHubConnection(string hubPath, string sessionId)
        => new HubConnectionBuilder()
            // Using 'access_token' query param for SignalR authentication compatibility
            .WithUrl($"{ApiBaseUrl}{hubPath}?access_token={sessionId}")
            .Build();

    protected async Task<HubConnection> ConnectToHubAsync(string hubPath, string sessionId)
    {
        var hub = CreateHubConnection(hubPath, sessionId);
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
        var (sessionId, expiresAt, userId) = await LoginAsUserAsync();
        return new UserSession(ApiBaseUrl, sessionId, expiresAt, userId);
    }

    /// <summary>
    /// Creates a new admin session with admin privileges.
    /// The session manages hub connections efficiently, reusing them across calls.
    /// </summary>
    protected async Task<UserSession> CreateAdminSessionAsync()
    {
        var (sessionId, expiresAt, userId) = await LoginAsAdminAsync();
        return new UserSession(ApiBaseUrl, sessionId, expiresAt, userId);
    }

    /// <summary>
    /// Creates a pre-authenticated HttpClient for admin endpoints.
    /// </summary>
    protected async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
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

    #endregion
}
