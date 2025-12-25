using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Contracts;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for authentication flow.
/// Tests login, logout, session management via SignalR AuthHub.
/// </summary>
[Collection("AppHost")]
public class AuthenticationTests : IntegrationTestBase
{
    public AuthenticationTests(AppHostFixture fixture) : base(fixture) { }

    #region Login Tests

    [Fact]
    public async Task Login_WithMockProvider_ReturnsSession()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var mockToken = $"mock:{userId}";

        // Act
        var (sessionId, expiresAt, returnedUserId) = await LoginAsync(mockToken);

        // Assert
        Assert.NotNull(sessionId);
        Assert.NotEmpty(sessionId);
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(userId, returnedUserId);
    }

    [Fact]
    public async Task Login_WithInvalidProvider_Returns400()
    {
        // Act
        var response = await HttpClient.PostAsJsonAsync("/api/auth/login", new
        {
            token = "invalid-token",
            provider = "NonExistentProvider"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_Invalidates_Session()
    {
        // Arrange - Login first
        var userId = Guid.NewGuid();
        var (sessionId, _, _) = await LoginAsync($"mock:{userId}");

        // Connect to AuthHub with the session
        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hub/auth?access_token={sessionId}")
            .Build();

        await authHub.StartAsync();

        try
        {
            // Act - Logout (invalidates session)
            await authHub.InvokeAsync("Logout");

            // After logout, creating a new connection with the same session should fail
            // when trying to call an authenticated method
            var authHub2 = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/hub/auth?access_token={sessionId}")
                .Build();

            try 
            {
                await authHub2.StartAsync();
                // Connection might succeed, but calling a protected method should fail
                await Assert.ThrowsAnyAsync<Exception>(async () => 
                    await authHub2.InvokeAsync<object>("GetProfile"));
            }
            finally
            {
                await authHub2.DisposeAsync();
            }
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_HTTP_ReturnsSessionInvalidated()
    {
        // 1. Arrange - Login first
        var userId = Guid.NewGuid();
        var (sessionId, _, _) = await LoginAsync($"mock:{userId}");

        // 2. Act - Logout via HTTP
        HttpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionId);
        
        var response = await HttpClient.PostAsync("/api/auth/logout", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LogoutResponse>();
        
        // 3. Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.True(result.SessionInvalidated);

        // 4. Second logout should return 401 Unauthorized (because session is gone)
        var secondResponse = await HttpClient.PostAsync("/api/auth/logout", null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, secondResponse.StatusCode);
    }

    #endregion

    #region Hub Authorization Tests

    [Fact]
    public async Task AuthHub_WithoutToken_CannotCallProtectedMethods()
    {
        // Arrange - create connection without token
        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hub/auth")
            .Build();

        await authHub.StartAsync();

        try
        {
            // Act & Assert - calling protected method should fail
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await authHub.InvokeAsync("Logout"));
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    #endregion

    #region Session Management Tests

    [Fact]
    public async Task RevokeAllSessions_InvalidatesAllUserSessions()
    {
        // 1. First login (Device A)
        var userId = Guid.NewGuid();
        var (sessionA, _, _) = await LoginAsync($"mock:{userId}");

        // 2. Second login (Device B) - Same user
        var (sessionB, _, _) = await LoginAsync($"mock:{userId}");

        // 3. Connect as Device A and Revoke All
        var authHubA = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hub/auth?access_token={sessionA}")
            .Build();
        await authHubA.StartAsync();
        
        try
        {
            var revokedCount = await authHubA.InvokeAsync<int>("RevokeAllSessions");
            Assert.True(revokedCount >= 2, $"Expected at least 2 sessions revoked, got {revokedCount}");
            
            // Wait for session invalidation to propagate
            await Task.Delay(100);

            // 4. Verify Session B is revoked by trying to call protected method
            var authHubB = new HubConnectionBuilder()
                .WithUrl($"{ApiBaseUrl}/hub/auth?access_token={sessionB}")
                .Build();

            try
            {
                await authHubB.StartAsync();
                // Connection might succeed, but calling a protected method should fail
                await Assert.ThrowsAnyAsync<Exception>(async () => 
                    await authHubB.InvokeAsync<object>("GetProfile"));
            }
            finally
            {
                await authHubB.DisposeAsync();
            }
        }
        finally
        {
            await authHubA.DisposeAsync();
        }
    }

    [Fact]
    public async Task LogoutAll_HTTP_InvalidatesAllSessions()
    {
        // 1. Login and get session
        var userId = Guid.NewGuid();
        var (sessionA, _, _) = await LoginAsync($"mock:{userId}");
        var (sessionB, _, _) = await LoginAsync($"mock:{userId}");

        // 2. Logout all via HTTP
        HttpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionA);
        
        var response = await HttpClient.PostAsync("/api/auth/logout-all", null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LogoutAllResult>();
        Assert.NotNull(result);
        Assert.True(result.SessionsInvalidated >= 2, $"Expected at least 2 sessions invalidated, got {result.SessionsInvalidated}");
        
        // 3. Verify sessions are invalidated by trying to call protected method
        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/hub/auth?access_token={sessionB}")
            .Build();

        try
        {
            await authHub.StartAsync();
            // Connection might succeed, but calling a protected method should fail
            await Assert.ThrowsAnyAsync<Exception>(async () => 
                await authHub.InvokeAsync<object>("GetProfile"));
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }   

    #endregion
}
