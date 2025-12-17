using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Titan.AppHost.Tests;

/// <summary>
/// Integration tests for the Base Types Admin API endpoints.
/// Tests CRUD operations, authorization, and error handling.
/// </summary>
[Collection("AppHost")]
public class BaseTypesAdminTests : IntegrationTestBase
{
    public BaseTypesAdminTests(AppHostFixture fixture) : base(fixture) { }

    #region Authorization Tests

    [Fact]
    public async Task GetBaseTypes_WithoutAuth_Returns401()
    {
        var response = await HttpClient.GetAsync("/api/admin/base-types");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateBaseType_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PostAsJsonAsync("/api/admin/base-types", new
        {
            baseTypeId = "test-type",
            name = "Test Type"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBaseType_WithoutAuth_Returns401()
    {
        var response = await HttpClient.PutAsJsonAsync("/api/admin/base-types/test-type", new
        {
            name = "Updated Type"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBaseType_WithoutAuth_Returns401()
    {
        var response = await HttpClient.DeleteAsync("/api/admin/base-types/test-type");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task GetAll_WithAuth_ReturnsBaseTypeList()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/base-types");
        
        response.EnsureSuccessStatusCode();
        var types = await response.Content.ReadFromJsonAsync<List<BaseTypeDto>>();
        Assert.NotNull(types);
        // Should have seeded base types
    }

    [Fact]
    public async Task CreateBaseType_WithAuth_ReturnsCreatedType()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var typeId = $"test-{Guid.NewGuid():N}"[..20];
        
        try
        {
            // Act - Use integer enum values matching the backend
            var response = await client.PostAsJsonAsync("/api/admin/base-types", new
            {
                baseTypeId = typeId,
                name = "Test Type",
                description = "A test base type",
                category = 0, // Equipment
                slot = 1, // MainHand
                width = 2,
                height = 3,
                maxStackSize = 1,
                isTradeable = true
            });
            
            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var baseType = await response.Content.ReadFromJsonAsync<BaseTypeDto>();
            Assert.NotNull(baseType);
            Assert.Equal(typeId, baseType.BaseTypeId);
            Assert.Equal("Test Type", baseType.Name);
        }
        finally
        {
            // Cleanup
            await client.DeleteAsync($"/api/admin/base-types/{typeId}");
        }
    }

    [Fact]
    public async Task GetById_ExistingType_ReturnsType()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var typeId = $"get-{Guid.NewGuid():N}"[..20];
        
        // Create first
        await client.PostAsJsonAsync("/api/admin/base-types", new
        {
            baseTypeId = typeId,
            name = "Get Test Type",
            category = 0,
            slot = 0,
            width = 1,
            height = 1,
            maxStackSize = 1,
            isTradeable = true
        });

        try
        {
            // Act
            var response = await client.GetAsync($"/api/admin/base-types/{typeId}");
            
            // Assert
            response.EnsureSuccessStatusCode();
            var baseType = await response.Content.ReadFromJsonAsync<BaseTypeDto>();
            Assert.NotNull(baseType);
            Assert.Equal(typeId, baseType.BaseTypeId);
        }
        finally
        {
            await client.DeleteAsync($"/api/admin/base-types/{typeId}");
        }
    }

    [Fact]
    public async Task GetById_NonExistentType_Returns404()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        
        var response = await client.GetAsync("/api/admin/base-types/nonexistent-type-id");
        
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBaseType_ExistingType_ReturnsUpdatedType()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var typeId = $"upd-{Guid.NewGuid():N}"[..20];
        
        // Create first
        await client.PostAsJsonAsync("/api/admin/base-types", new
        {
            baseTypeId = typeId,
            name = "Original Name",
            description = "Original Description",
            category = 0,
            slot = 0,
            width = 1,
            height = 1,
            maxStackSize = 1,
            isTradeable = true
        });

        try
        {
            // Act
            var response = await client.PutAsJsonAsync($"/api/admin/base-types/{typeId}", new
            {
                name = "Updated Name",
                description = "Updated Description",
                category = 1, // Currency
                slot = 0, // None
                width = 1,
                height = 1,
                maxStackSize = 100,
                isTradeable = false
            });
            
            // Assert
            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<BaseTypeDto>();
            Assert.NotNull(updated);
            Assert.Equal("Updated Name", updated.Name);
            Assert.Equal("Updated Description", updated.Description);
            Assert.Equal(100, updated.MaxStackSize);
        }
        finally
        {
            await client.DeleteAsync($"/api/admin/base-types/{typeId}");
        }
    }

    [Fact]
    public async Task DeleteBaseType_ExistingType_ReturnsNoContent()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var typeId = $"del-{Guid.NewGuid():N}"[..20];
        
        // Create first
        await client.PostAsJsonAsync("/api/admin/base-types", new
        {
            baseTypeId = typeId,
            name = "Delete Test Type",
            category = 0,
            slot = 0,
            width = 1,
            height = 1,
            maxStackSize = 1,
            isTradeable = true
        });

        // Act
        var response = await client.DeleteAsync($"/api/admin/base-types/{typeId}");
        
        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        
        // Verify deleted
        var getResponse = await client.GetAsync($"/api/admin/base-types/{typeId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateBaseType_FullProperties_AllPropertiesPersisted()
    {
        using var client = await CreateAuthenticatedAdminClientAsync();
        var typeId = $"full-{Guid.NewGuid():N}"[..20];
        
        try
        {
            // Act - Create with all properties
            await client.PostAsJsonAsync("/api/admin/base-types", new
            {
                baseTypeId = typeId,
                name = "Full Props Type",
                description = "Has all properties",
                category = 0, // Equipment
                slot = 2, // Helmet
                width = 2,
                height = 2,
                maxStackSize = 1,
                isTradeable = false
            });
            
            // Get and verify
            var response = await client.GetAsync($"/api/admin/base-types/{typeId}");
            var baseType = await response.Content.ReadFromJsonAsync<BaseTypeDto>();
            
            Assert.NotNull(baseType);
            Assert.Equal("Full Props Type", baseType.Name);
            Assert.Equal("Has all properties", baseType.Description);
            Assert.Equal(0, baseType.Category); // Equipment
            Assert.Equal(2, baseType.Slot); // Helmet
            Assert.Equal(2, baseType.Width);
            Assert.Equal(2, baseType.Height);
            Assert.Equal(1, baseType.MaxStackSize);
            Assert.False(baseType.IsTradeable);
        }
        finally
        {
            await client.DeleteAsync($"/api/admin/base-types/{typeId}");
        }
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
            new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private record AdminLoginResponse(
        bool Success,
        string UserId,
        string Email,
        string? DisplayName,
        List<string> Roles,
        string AccessToken,
        string RefreshToken,
        int ExpiresInSeconds);

    // Use integers for enum types since API returns them as numbers
    private record BaseTypeDto
    {
        public string BaseTypeId { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public int Category { get; init; } // 0=Equipment, 1=Currency, etc.
        public int Slot { get; init; } // 0=None, 1=MainHand, 2=Helmet, etc.
        public int Width { get; init; }
        public int Height { get; init; }
        public int MaxStackSize { get; init; }
        public bool IsTradeable { get; init; }
    }

    #endregion
}
