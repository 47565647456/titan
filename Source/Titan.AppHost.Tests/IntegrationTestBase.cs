using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Titan.Abstractions.Models;

namespace Titan.AppHost.Tests;

/// <summary>
/// Shared fixture that starts ONE AppHost and shares it across all tests.
/// This avoids port conflicts and speeds up test execution.
/// </summary>
public class AppHostFixture : DistributedApplicationFactory, IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    
    // We only need the base constructor that takes the entry point type
    public AppHostFixture() : base(typeof(Projects.Titan_AppHost)) { }

    public DistributedApplication App { get; private set; } = null!;
    public string ApiBaseUrl { get; private set; } = null!;

    protected override void OnBuilderCreating(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostOptions)
    {
        // Pass configuration via command line args to ensure it overrides local defaults
        applicationOptions.Args = new[] { "PostgresVolume=ephemeral" };
        
        hostOptions.Configuration ??= new();
        // Ensure we are in Development mode to avoid production validation failures
        hostOptions.EnvironmentName = "Development";
    }

    protected override void OnBuilding(DistributedApplicationBuilder builder)
    {
        // Add resilience to HTTP clients
        builder.Services.ConfigureHttpClientDefaults(http => 
            http.AddStandardResilienceHandler());
            
        base.OnBuilding(builder);
    }

    protected override void OnBuilt(DistributedApplication app)
    {
        App = app;
        base.OnBuilt(app);
    }

    public async Task InitializeAsync()
    {
        // Start the application using the factory
        await StartAsync();
            
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

    // DistributedApplicationFactory handles disposal, but IAsyncLifetime requires DisposeAsync.
    // We should call base.DisposeAsync if exposing the factory directly, or handle App disposal here.
    // Since we manually built 'App', we should dispose it.
    public new async Task DisposeAsync()
    {
        if (App != null)
        {
            await App.DisposeAsync();
        }
        await base.DisposeAsync();
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
