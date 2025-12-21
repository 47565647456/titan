using System.Net;
using System.Net.Http.Json;
using Titan.Abstractions.Contracts;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Sessions Admin API endpoints.
/// Tests session listing, invalidation, and authorization.
/// </summary>
[Collection("AppHost")]
public class SessionsAdminTests : IntegrationTestBase
{
    public SessionsAdminTests(AppHostFixture fixture) : base(fixture) { }

    #region Authorization Tests

    [Fact]
    public async Task GetSessions_WithoutAuth_Returns401()
    {
        // Arrange - no auth

        // Act
        var response = await HttpClient.GetAsync("/api/admin/sessions");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSessionCount_WithoutAuth_Returns401()
    {
        // Arrange - no auth

        // Act
        var response = await HttpClient.GetAsync("/api/admin/sessions/count");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidateSession_WithoutAuth_Returns401()
    {
        // Arrange - no auth

        // Act
        var response = await HttpClient.DeleteAsync("/api/admin/sessions/some-ticket-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Session Listing Tests

    [Fact]
    public async Task GetSessions_WithAuth_ReturnsSessionList()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/sessions");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionListDto>();
        Assert.NotNull(result);
        Assert.NotNull(result.Sessions);
        Assert.True(result.TotalCount >= 1); // Should have at least our own session
    }

    [Fact]
    public async Task GetSessionCount_WithAuth_ReturnsCount()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/sessions/count");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionCountDto>();
        Assert.NotNull(result);
        Assert.True(result.Count >= 1); // Should have at least our own session
    }

    [Fact]
    public async Task GetSessions_Pagination_ReturnsPagedResults()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.GetAsync("/api/admin/sessions?skip=0&take=10");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionListDto>();
        Assert.NotNull(result);
        Assert.Equal(0, result.Skip);
        Assert.Equal(10, result.Take);
    }

    #endregion

    #region Session Invalidation Tests

    [Fact]
    public async Task InvalidateSession_NonExistent_ReturnsSuccessFalse()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();

        // Act
        var response = await client.DeleteAsync("/api/admin/sessions/nonexistent-ticket-id");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvalidateSessionResultDto>();
        Assert.NotNull(result);
        Assert.False(result.Success); // Session didn't exist
    }

    #endregion

    #region User Sessions Tests

    [Fact]
    public async Task GetUserSessions_WithAuth_ReturnsSessionsForUser()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();
        var randomUserId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/admin/sessions/user/{randomUserId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<List<SessionInfoDto>>();
        Assert.NotNull(result);
        // Random user should have no sessions
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserSessions_WithoutAuth_Returns401()
    {
        // Arrange - no auth
        var randomUserId = Guid.NewGuid();

        // Act
        var response = await HttpClient.GetAsync($"/api/admin/sessions/user/{randomUserId}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidateUserSessions_WithAuth_ReturnsCount()
    {
        // Arrange
        using var client = await CreateAuthenticatedAdminClientAsync();
        var randomUserId = Guid.NewGuid();

        // Act - Invalid user should return 0 invalidated
        var response = await client.DeleteAsync($"/api/admin/sessions/user/{randomUserId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvalidateAllSessionsResultDto>();
        Assert.NotNull(result);
        Assert.Equal(0, result.Count); // No sessions for random user
    }

    [Fact]
    public async Task InvalidateUserSessions_WithoutAuth_Returns401()
    {
        // Arrange - no auth
        var randomUserId = Guid.NewGuid();

        // Act
        var response = await HttpClient.DeleteAsync($"/api/admin/sessions/user/{randomUserId}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion
}
