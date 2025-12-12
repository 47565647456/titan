using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
}
