using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using Xunit;

namespace Titan.AppHost.Tests;

/// <summary>
/// Tests for input validation across API endpoints.
/// Verifies that FluentValidation rejects invalid inputs with 400 responses.
/// </summary>
[Collection("AppHost")]
public class ValidationTests : IntegrationTestBase
{
    private readonly HttpClient _client;
    private string? _accessToken;

    public ValidationTests(AppHostFixture fixture) : base(fixture)
    {
        _client = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
    }

    private async Task EnsureAdminLoginAsync()
    {
        if (_accessToken != null) return;

        // Reset lockout state before attempting login
        await Fixture.ResetAdminLockoutAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Email = "admin@titan.local",
            Password = "Admin123!"
        });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AdminLoginResult>();
            _accessToken = result?.AccessToken;
            if (!string.IsNullOrEmpty(_accessToken))
            {
                _client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
            }
        }
    }

    private record AdminLoginResult(string? AccessToken, string? RefreshToken);

    #region Admin Login Validation

    [Fact]
    public async Task AdminLogin_EmptyEmail_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Email = "",
            Password = "ValidPassword123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLogin_InvalidEmailFormat_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Email = "not-an-email",
            Password = "ValidPassword123!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("email", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLogin_EmptyPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Email = "test@example.com",
            Password = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Password", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminLogin_ShortPassword_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/admin/auth/login", new
        {
            Email = "test@example.com",
            Password = "short"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("8", content); // Should mention minimum 8 characters
    }

    #endregion

    #region Season Validation

    [Fact]
    public async Task CreateSeason_EmptySeasonId_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/seasons", new
        {
            SeasonId = "",
            Name = "Test Season",
            StartDate = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("SeasonId", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSeason_EmptyName_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/seasons", new
        {
            SeasonId = "test-season",
            Name = "",
            StartDate = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSeason_OversizedId_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var oversizedId = new string('x', 200);
        var response = await _client.GetAsync($"/api/admin/seasons/{oversizedId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Base Type Validation

    [Fact]
    public async Task CreateBaseType_EmptyId_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/base-types", new
        {
            BaseTypeId = "",
            Name = "Test Base Type"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("BaseTypeId", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBaseType_InvalidIdFormat_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/base-types", new
        {
            BaseTypeId = "invalid id with spaces!@#",
            Name = "Test Base Type"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("BaseTypeId", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBaseType_OversizedId_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var oversizedId = new string('x', 200);
        var response = await _client.PostAsJsonAsync("/api/admin/base-types", new
        {
            BaseTypeId = oversizedId,
            Name = "Test Base Type"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("100", content); // Should mention max length of 100
    }

    [Fact]
    public async Task CreateBaseType_InvalidDimensions_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/base-types", new
        {
            BaseTypeId = "valid-id",
            Name = "Test Base Type",
            Width = 0,
            Height = 200
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        // Should mention either Width or Height validation error
        Assert.True(
            content.Contains("Width", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Height", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Rate Limit Policy Validation

    [Fact]
    public async Task UpsertPolicy_EmptyName_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/rate-limiting/policies", new
        {
            Name = "",
            Rules = new[] { "10/1m" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("name", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertPolicy_EmptyRules_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/rate-limiting/policies", new
        {
            Name = "test-policy",
            Rules = Array.Empty<string>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("rule", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddEndpointMapping_EmptyPattern_ReturnsBadRequest()
    {
        await EnsureAdminLoginAsync();

        var response = await _client.PostAsJsonAsync("/api/admin/rate-limiting/mappings", new
        {
            Pattern = "",
            PolicyName = "Global"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pattern", content, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
