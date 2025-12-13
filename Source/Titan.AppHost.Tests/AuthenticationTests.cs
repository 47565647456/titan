using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Titan.AppHost.Tests;

[Collection("AppHost")]
public class AuthenticationTests : IntegrationTestBase
{
    public AuthenticationTests(AppHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Can_Login_And_Get_Valid_JWT()
    {
        // Act - Use CreateUserSessionAsync to test login flow
        await using var user = await CreateUserSessionAsync();

        // Assert
        Assert.NotNull(user.Token);
        Assert.NotEqual(Guid.Empty, user.UserId);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(user.Token);

        Assert.Equal("Titan", jwtToken.Issuer);
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == user.UserId.ToString());
    }

    [Fact]
    public async Task Admin_Login_Grants_Admin_Role()
    {
        // Act
        await using var admin = await CreateAdminSessionAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(admin.Token);

        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task Standard_User_Login_Has_No_Admin_Role()
    {
        // Act
        await using var user = await CreateUserSessionAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(user.Token);

        Assert.DoesNotContain(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task Hub_Connection_Enforces_Authorization()
    {
        // 1. Verify Unauthenticated uses are Rejected
        // Note: SignalR connection might succeed but invocation fails, OR connection fails depending on config.
        // Our AuthHub is unshielded, but AccountHub is [Authorize].
        
        var unauthenticatedConnection = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub")
            .Build();

        // Expectation: Connection might fail outright with 401, or succeed but methods fail.
        // Default AspNetCore behaviour for [Authorize] on Hub class is to reject handshake with 401.
        
        await Assert.ThrowsAnyAsync<Exception>(async () => await unauthenticatedConnection.StartAsync());

        // 2. Verify Authenticated user CAN connect
        await using var user = await CreateUserSessionAsync();
        var hub = await user.GetAccountHubAsync();
        Assert.Equal(HubConnectionState.Connected, hub.State);
    }

    #region Token Refresh Tests

    [Fact]
    public async Task Login_Returns_RefreshToken()
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
    public async Task RefreshToken_Returns_NewTokens()
    {
        // Arrange - Login and get initial tokens
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();

        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/authHub?access_token={accessToken}")
            .Build();

        await authHub.StartAsync();

        try
        {
            // Act - Refresh tokens
            var result = await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken, userId);

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
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();

        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/authHub?access_token={accessToken}")
            .Build();

        await authHub.StartAsync();

        try
        {
            // Act - Use refresh token once
            var result = await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken, userId);
            Assert.NotNull(result);

            // Try to use the OLD refresh token again - should fail (rotation)
            await Assert.ThrowsAsync<HubException>(async () =>
                await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken, userId));
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
        var (accessToken, refreshToken, expiresIn, userId) = await LoginAsUserAsync();

        var authHub = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/authHub?access_token={accessToken}")
            .Build();

        await authHub.StartAsync();

        try
        {
            // Act - Logout (revokes refresh token)
            await authHub.InvokeAsync("Logout", refreshToken);

            // Try to use the revoked refresh token - should fail
            await Assert.ThrowsAsync<HubException>(async () =>
                await authHub.InvokeAsync<RefreshResult>("RefreshToken", refreshToken, userId));
        }
        finally
        {
            await authHub.DisposeAsync();
        }
    }

    #endregion
}

/// <summary>
/// Result of a token refresh operation.
/// </summary>
public record RefreshResult(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds);
