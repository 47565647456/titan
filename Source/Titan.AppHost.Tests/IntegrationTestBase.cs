using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Titan.Abstractions.Models;

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

    public async Task InitializeAsync()
    {
        // Create the AppHost using DistributedApplicationTestingBuilder with configuration as arguments
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Titan_AppHost>(
            [
                "--environment=Development",
                "Database:Volume=ephemeral",
                "Parameters:cockroachdb-password=TestPassword123!"
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
            App.ResourceNotifications.WaitForResourceHealthyAsync("api")
        ).WaitAsync(DefaultTimeout);
        
        // Give Orleans cluster time to stabilize
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        // Get API endpoint
        var endpoint = App.GetEndpoint("api", "https");
        ApiBaseUrl = endpoint.ToString().TrimEnd('/');
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

    protected IntegrationTestBase(AppHostFixture fixture)
    {
        Fixture = fixture;
    }

    #region Authentication Helpers

    /// <summary>
    /// Login via AuthHub and return the JWT token.
    /// </summary>
    protected async Task<(string Token, Guid UserId)> LoginAsync(string mockToken, string provider = "Mock")
    {
        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/authHub")
            .Build();
        
        try
        {
            await authHub.StartAsync();
            var result = await authHub.InvokeAsync<LoginResult>("Login", mockToken, provider);
            
            if (!result.Success || string.IsNullOrEmpty(result.Token))
                throw new InvalidOperationException($"Login failed: {result.ErrorMessage}");
            
            return (result.Token, result.UserId!.Value);
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    protected Task<(string Token, Guid UserId)> LoginAsUserAsync()
        => LoginAsync($"mock:{Guid.NewGuid()}");

    protected Task<(string Token, Guid UserId)> LoginAsAdminAsync()
        => LoginAsync($"mock:admin:{Guid.NewGuid()}");

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
    /// Ensures a test item type exists in the registry. Requires admin token.
    /// </summary>
    protected async Task EnsureItemTypeExistsAsync(string adminToken, string itemTypeId, bool isTradeable = true)
    {
        var hub = await ConnectToHubAsync("/itemTypeHub", adminToken);
        try
        {
            // Try to get it first
            var existing = await hub.InvokeAsync<ItemTypeDefinition?>("Get", itemTypeId);
            if (existing != null) return;
        }
        catch
        {
            // Item type doesn't exist, create it
        }
        
        var definition = new ItemTypeDefinition
        {
            ItemTypeId = itemTypeId,
            Name = itemTypeId.Replace("_", " "),
            MaxStackSize = 1,
            IsTradeable = isTradeable,
            Category = "test"
        };
        
        await hub.InvokeAsync<ItemTypeDefinition>("Create", definition);
        await hub.DisposeAsync();
    }

    #endregion
}

/// <summary>
/// LoginResult record matching AuthHub response.
/// </summary>
public record LoginResult(
    bool Success, 
    Guid? UserId, 
    string? Provider, 
    UserIdentity? Identity, 
    string? Token, 
    string? ErrorMessage);
