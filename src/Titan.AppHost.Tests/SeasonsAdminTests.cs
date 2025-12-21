using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Seasons Admin API endpoints.
/// Tests CRUD operations, authorization, and error handling.
/// </summary>
[Collection("AppHost")]
public class SeasonsAdminTests : IntegrationTestBase
{
    public SeasonsAdminTests(AppHostFixture fixture) : base(fixture) { }

    #region Authorization Tests

    [Fact]
    public async Task GetSeasons_WithoutAuth_Returns401()
    {
        var response = await HttpClient.GetAsync("/api/admin/seasons");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSeason_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PostAsJsonAsync("/api/admin/seasons", new
        {
            seasonId = "test-season",
            name = "Test Season"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSeasonStatus_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PutAsJsonAsync("/api/admin/seasons/test/status", new
        {
            status = 1 // Active (as integer)
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndSeason_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PostAsync("/api/admin/seasons/test/end", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task GetAll_WithAuth_ReturnsSeasonList()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/seasons");
        
        response.EnsureSuccessStatusCode();
        var seasons = await response.Content.ReadFromJsonAsync<List<SeasonDto>>();
        Assert.NotNull(seasons);
        // Should have the seeded "standard" season
        Assert.Contains(seasons, s => s.SeasonId == "standard");
    }

    [Fact]
    public async Task CreateSeason_WithAuth_ReturnsCreatedSeason()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var seasonId = $"test-{Guid.NewGuid():N}"[..20];
        
        // Act - Use integer enum values matching the backend
        var response = await client.PostAsJsonAsync("/api/admin/seasons", new
        {
            seasonId = seasonId,
            name = "Test Season",
            type = 1, // Temporary
            status = 0, // Upcoming
            startDate = DateTimeOffset.UtcNow.AddDays(1),
            endDate = DateTimeOffset.UtcNow.AddDays(30),
            migrationTargetId = "standard",
            isVoid = false
        });
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var season = await response.Content.ReadFromJsonAsync<SeasonDto>();
        Assert.NotNull(season);
        Assert.Equal(seasonId, season.SeasonId);
        Assert.Equal("Test Season", season.Name);
    }

    [Fact]
    public async Task GetById_ExistingSeason_ReturnsSeason()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Get the standard season
        var response = await client.GetAsync("/api/admin/seasons/standard");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var season = await response.Content.ReadFromJsonAsync<SeasonDto>();
        Assert.NotNull(season);
        Assert.Equal("standard", season.SeasonId);
    }

    [Fact]
    public async Task GetById_NonExistentSeason_Returns404()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/seasons/nonexistent-season-id");
        
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ExistingSeason_ReturnsSuccess()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var seasonId = $"status-{Guid.NewGuid():N}"[..20];
        
        // Create a season first
        var createResponse = await client.PostAsJsonAsync("/api/admin/seasons", new
        {
            seasonId = seasonId,
            name = "Status Test",
            type = 1, // Temporary
            status = 0, // Upcoming
            startDate = DateTimeOffset.UtcNow.AddDays(1),
            migrationTargetId = "standard"
        });
        createResponse.EnsureSuccessStatusCode();
        
        // Act - Update status (use integer for enum)
        var response = await client.PutAsJsonAsync($"/api/admin/seasons/{seasonId}/status", new
        {
            status = 1 // Active
        });
        
        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify the change
        var getResponse = await client.GetAsync($"/api/admin/seasons/{seasonId}");
        var season = await getResponse.Content.ReadFromJsonAsync<SeasonDto>();
        Assert.Equal(1, season!.Status); // Active = 1
    }

    [Fact]
    public async Task EndSeason_ExistingSeason_ReturnsSuccess()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var seasonId = $"end-{Guid.NewGuid():N}"[..20];
        
        // Create a season and set it to active first
        await client.PostAsJsonAsync("/api/admin/seasons", new
        {
            seasonId = seasonId,
            name = "End Test",
            type = 1, // Temporary
            status = 1, // Active
            startDate = DateTimeOffset.UtcNow.AddDays(-1),
            migrationTargetId = "standard"
        });
        
        // Act - End the season
        var response = await client.PostAsync($"/api/admin/seasons/{seasonId}/end", null);
        
        // Assert
        response.EnsureSuccessStatusCode();
        
        // Verify it's ended
        var getResponse = await client.GetAsync($"/api/admin/seasons/{seasonId}");
        var season = await getResponse.Content.ReadFromJsonAsync<SeasonDto>();
        Assert.Equal(2, season!.Status); // Ended = 2
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

    // Use integers for enum types since API returns them as numbers
    private record SeasonDto
    {
        public string SeasonId { get; init; } = "";
        public string Name { get; init; } = "";
        public int Type { get; init; } // 0=Permanent, 1=Temporary
        public int Status { get; init; } // 0=Upcoming, 1=Active, 2=Ended, etc.
        public DateTimeOffset StartDate { get; init; }
        public DateTimeOffset? EndDate { get; init; }
        public string MigrationTargetId { get; init; } = "";
        public bool IsVoid { get; init; }
    }

    #endregion
}
