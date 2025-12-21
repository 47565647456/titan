using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Accounts Admin API endpoints.
/// Tests CRUD operations, authorization, and error handling.
/// </summary>
[Collection("AppHost")]
public class AccountsAdminTests : IntegrationTestBase
{
    public AccountsAdminTests(AppHostFixture fixture) : base(fixture) { }

    #region Authorization Tests

    [Fact]
    public async Task GetAccounts_WithoutAuth_Returns401()
    {
        var response = await HttpClient.GetAsync("/api/admin/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAccountById_WithoutAuth_Returns401()
    {
        var response = await HttpClient.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PostAsync("/api/admin/accounts", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithoutAuth_Returns401()
    {
        var response = await HttpClient.DeleteAsync($"/api/admin/accounts/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminLogin_ExceedingFailedAttempts_LocksOutUser()
    {
        // First ensure admin is not locked out
        await Fixture.ResetAdminLockoutAsync();
        
        try
        {
            // Make 6 failed login attempts (lockout is configured at 5 failed attempts)
            for (int i = 0; i < 6; i++)
            {
                await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
                {
                    email = "admin@titan.local",
                    password = "WrongPassword123!"
                });
            }
            
            // Now try with correct password - should be locked out
            var response = await HttpClient.PostAsJsonAsync("/api/admin/auth/login", new
            {
                email = "admin@titan.local",
                password = "Admin123!"
            });
            
            // Should return 401 due to lockout
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            
            // Verify the error message indicates lockout
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("locked", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Always reset the lockout to not affect other tests
            await Fixture.ResetAdminLockoutAsync();
        }
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task GetAll_WithAuth_ReturnsAccountList()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/accounts");
        
        response.EnsureSuccessStatusCode();
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountSummaryDto>>();
        Assert.NotNull(accounts);
    }

    [Fact]
    public async Task CreateAccount_WithAuth_ReturnsCreatedAccount()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Create account
        var response = await client.PostAsync("/api/admin/accounts", null);
        
        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<AccountDetailDto>();
        Assert.NotNull(account);
        Assert.NotEqual(Guid.Empty, account.AccountId);
    }

    [Fact]
    public async Task GetById_AfterCreate_ReturnsAccount()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Create an account first
        var createResponse = await client.PostAsync("/api/admin/accounts", null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<AccountDetailDto>();
        
        // Act - Get by ID
        var response = await client.GetAsync($"/api/admin/accounts/{created!.AccountId}");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountDetailDto>();
        Assert.NotNull(account);
        Assert.Equal(created.AccountId, account.AccountId);
    }

    [Fact]
    public async Task GetById_NonExistentAccount_Returns404()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Try to get an account that doesn't exist
        var response = await client.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}");
        
        // Assert - Should return 404, not auto-create
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCharacters_NonExistentAccount_Returns404()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Try to get characters for an account that doesn't exist
        var response = await client.GetAsync($"/api/admin/accounts/{Guid.NewGuid()}/characters");
        
        // Assert - Should return 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCharacters_AfterCreate_ReturnsEmptyList()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Create an account first
        var createResponse = await client.PostAsync("/api/admin/accounts", null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<AccountDetailDto>();
        
        // Act - Get characters
        var response = await client.GetAsync($"/api/admin/accounts/{created!.AccountId}/characters");
        
        // Assert
        response.EnsureSuccessStatusCode();
        var characters = await response.Content.ReadFromJsonAsync<List<CharacterSummaryDto>>();
        Assert.NotNull(characters);
        // New account should have no characters
        Assert.Empty(characters);
    }

    [Fact]
    public async Task DeleteAccount_NonExistentAccount_Returns404()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Act - Delete an account that was never created via the proper channels
        var response = await client.DeleteAsync($"/api/admin/accounts/{Guid.NewGuid()}");
        
        // Assert - Should return 404 since it's not in the account index
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_CreatedAccount_ReturnsNoContent()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        // Create an account first
        var createResponse = await client.PostAsync("/api/admin/accounts", null);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<AccountDetailDto>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.AccountId);
        
        // Small delay to ensure grain state is persisted
        await Task.Delay(100);
        
        // Act - Delete the created account
        var deleteResponse = await client.DeleteAsync($"/api/admin/accounts/{created.AccountId}");
        
        // Assert - Should return 204 NoContent
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        
        // Verify it's actually deleted - should return 404 now
        var getResponse = await client.GetAsync($"/api/admin/accounts/{created.AccountId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    #endregion

    #region Helpers

    private record AccountSummaryDto(Guid AccountId, DateTimeOffset CreatedAt, int CharacterCount);
    
    private record AccountDetailDto
    {
        public Guid AccountId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public List<string> UnlockedCosmetics { get; init; } = [];
        public List<string> UnlockedAchievements { get; init; } = [];
    }

    private record CharacterSummaryDto
    {
        public Guid CharacterId { get; init; }
        public string Name { get; init; } = "";
        public string SeasonId { get; init; } = "";
        public int Level { get; init; }
        public bool IsDead { get; init; }
        public string Restrictions { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
    }

    #endregion
}
