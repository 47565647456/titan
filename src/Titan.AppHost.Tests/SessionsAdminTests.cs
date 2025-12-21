using System.Net;
using System.Net.Http.Json;

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
        var response = await HttpClient.GetAsync("/api/admin/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSessionCount_WithoutAuth_Returns401()
    {
        var response = await HttpClient.GetAsync("/api/admin/sessions/count");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidateSession_WithoutAuth_Returns401()
    {
        var response = await HttpClient.DeleteAsync("/api/admin/sessions/some-ticket-id");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Session Listing Tests

    [Fact]
    public async Task GetSessions_WithAuth_ReturnsSessionList()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/sessions");
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Sessions);
        // Should have at least our own session
        Assert.True(result.TotalCount >= 1);
    }

    [Fact]
    public async Task GetSessionCount_WithAuth_ReturnsCount()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/sessions/count");
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionCountResponse>();
        Assert.NotNull(result);
        // Should have at least our own session
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public async Task GetSessions_Pagination_ReturnsPagedResults()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Request with pagination
        var response = await client.GetAsync("/api/admin/sessions?skip=0&take=10");
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionListResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.Skip);
        Assert.Equal(10, result.Take);
    }

    #endregion

    #region Session Invalidation Tests

    [Fact]
    public async Task InvalidateSession_NonExistent_ReturnsSuccess()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Try to invalidate a non-existent session - should return success: false
        var response = await client.DeleteAsync("/api/admin/sessions/nonexistent-ticket-id");
        
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvalidateResponse>();
        Assert.NotNull(result);
        Assert.False(result.Success); // Session didn't exist
    }

    #endregion

    #region DTOs

    private record SessionListResponse
    {
        public List<SessionInfo> Sessions { get; init; } = [];
        public int TotalCount { get; init; }
        public int Skip { get; init; }
        public int Take { get; init; }
    }

    private record SessionInfo
    {
        public string TicketId { get; init; } = "";
        public string UserId { get; init; } = "";
        public string Provider { get; init; } = "";
        public List<string> Roles { get; init; } = [];
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset LastActivityAt { get; init; }
        public bool IsAdmin { get; init; }
    }

    private record SessionCountResponse
    {
        public int Count { get; init; }
    }

    private record InvalidateResponse
    {
        public bool Success { get; init; }
    }

    #endregion
}
