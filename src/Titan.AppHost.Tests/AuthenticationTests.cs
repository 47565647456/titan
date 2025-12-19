using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;
using Xunit;

namespace Titan.AppHost.Tests;

[Collection("AppHost")]
public class AuthenticationTests : IntegrationTestBase
{
    public AuthenticationTests(AppHostFixture fixture) : base(fixture)
    {
    }

    #region HTTP Login Tests

    [Fact]
    public async Task Can_Login_Via_HTTP_And_Get_Valid_JWT()
    {
        // Act - Use HTTP login endpoint
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();

        // Assert
        Assert.NotNull(accessToken);
        Assert.NotEqual(Guid.Empty, userId);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(accessToken);

        Assert.Equal("Titan", jwtToken.Issuer);
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
    }

    [Fact]
    public async Task Admin_Login_Grants_Admin_Role()
    {
        // Act
        var (accessToken, _, _, _) = await LoginAsAdminAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(accessToken);

        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task Standard_User_Login_Has_No_Admin_Role()
    {
        // Act
        var (accessToken, _, _, _) = await LoginAsUserAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(accessToken);

        Assert.DoesNotContain(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task HTTP_Login_Returns_RefreshToken()
    {
        // Act
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();

        // Assert
        Assert.NotNull(accessToken);
        Assert.NotNull(refreshToken);
        Assert.NotEmpty(refreshToken);
        Assert.True(expiresIn > 0, "Access token expiry should be positive");
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task HTTP_Refresh_Returns_NewTokens()
    {
        // Arrange - Login and get initial tokens
        var (accessToken, refreshToken, _, userId) = await LoginAsUserAsync();

        // Act - Refresh tokens via HTTP
        var request = new { refreshToken, userId };
        var response = await HttpClient.PostAsJsonAsync("/api/auth/refresh", request);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<RefreshResult>();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result!.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.True(result.AccessTokenExpiresInSeconds > 0);

        // New tokens should be different from original
        Assert.NotEqual(accessToken, result.AccessToken);
        Assert.NotEqual(refreshToken, result.RefreshToken);
    }

    [Fact]
    public async Task GetProviders_Returns_Available_Providers()
    {
        // Act
        var response = await HttpClient.GetAsync("/api/auth/providers");
        response.EnsureSuccessStatusCode();
        
        var providers = await response.Content.ReadFromJsonAsync<List<string>>();

        // Assert
        Assert.NotNull(providers);
        Assert.NotEmpty(providers);
        Assert.Contains("Mock", providers);
    }

    #endregion

    #region Hub Authorization Tests

    [Fact]
    public async Task Hub_Connection_Enforces_Authorization()
    {
        // 1. Verify Unauthenticated users are Rejected
        var unauthenticatedConnection = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub")
            .Build();

        // Default AspNetCore behaviour for [Authorize] on Hub class is to reject handshake with 401.
        await Assert.ThrowsAnyAsync<Exception>(async () => await unauthenticatedConnection.StartAsync());

        // 2. Verify Authenticated user CAN connect
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetAccountHubAsync();
        Assert.Equal(HubConnectionState.Connected, hub.State);
    }

    #endregion

    #region WebSocket Token Refresh Tests

    [Fact]
    public async Task WebSocket_RefreshToken_Returns_NewTokens()
    {
        // Arrange - Login via HTTP and get initial tokens
        var (accessToken, refreshToken, _, userId) = await LoginAsUserAsync();

        // Connect to AuthHub with the access token using ticket-based auth
        var authHub = await CreateHubConnectionAsync("/authHub", accessToken);
        await authHub.StartAsync();

        try
        {
            // Act - Refresh tokens via WebSocket (new signature: only refreshToken, no userId)
            var result = await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.AccessToken);
            Assert.NotNull(result.RefreshToken);
            Assert.True(result.AccessTokenExpiresInSeconds > 0);

            // New tokens should be different from original
            Assert.NotEqual(accessToken, result.AccessToken);
            Assert.NotEqual(refreshToken, result.RefreshToken);
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefreshToken_Rotates_Token_OldTokenFails()
    {
        // Arrange - Login and get initial tokens
        var (accessToken, refreshToken, _, _) = await LoginAsUserAsync();

        var authHub = await CreateHubConnectionAsync("/authHub", accessToken);
        await authHub.StartAsync();

        try
        {
            // Act - Use refresh token once
            var result = await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken);
            Assert.NotNull(result);

            // Try to use the OLD refresh token again - should fail (rotation)
            await Assert.ThrowsAsync<HubException>(async () =>
                await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken));
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_Revokes_RefreshToken()
    {
        // Arrange - Login and get tokens
        var (accessToken, refreshToken, _, _) = await LoginAsUserAsync();

        var authHub = await CreateHubConnectionAsync("/authHub", accessToken);
        await authHub.StartAsync();

        try
        {
            // Act - Logout (revokes refresh token)
            await authHub.InvokeAsync("Logout", refreshToken);

            // Try to use the revoked refresh token - should fail
            await Assert.ThrowsAsync<HubException>(async () =>
                await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken));
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    [Fact]
    public async Task RevokeAllTokens_InvalidatesForAllSessions()
    {
        // 1. First login (Device A)
        var userId = Guid.NewGuid();
        var (tokenA, refreshTokenA, _, _) = await LoginAsync($"mock:{userId}");

        // 2. Second login (Device B) - Same user
        var (tokenB, refreshTokenB, _, _) = await LoginAsync($"mock:{userId}");

        // 3. Connect as Device A and Revoke All using ticket-based auth
        var authHubA = await CreateHubConnectionAsync("/authHub", tokenA);
        await authHubA.StartAsync();
        
        try
        {
            await authHubA.InvokeAsync("RevokeAllTokens");

            // 4. Verify Token A is revoked
            await Assert.ThrowsAsync<HubException>(async () => 
                await authHubA.InvokeAsync<RefreshResult>("RefreshToken", refreshTokenA));
                
            // 5. Verify Token B is revoked
            await Assert.ThrowsAsync<HubException>(async () => 
                await authHubA.InvokeAsync<RefreshResult>("RefreshToken", refreshTokenB));
        }
        finally
        {
            await authHubA.DisposeAsync();
        }
    }

    #endregion
}
