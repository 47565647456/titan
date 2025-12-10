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
        // Act
        var (token, userId) = await LoginAsUserAsync();

        // Assert
        Assert.NotNull(token);
        Assert.NotEqual(Guid.Empty, userId);

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        Assert.Equal("Titan", jwtToken.Issuer);
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == userId.ToString());
    }

    [Fact]
    public async Task Admin_Login_Grants_Admin_Role()
    {
        // Act
        var (token, _) = await LoginAsAdminAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task Standard_User_Login_Has_No_Admin_Role()
    {
        // Act
        var (token, _) = await LoginAsUserAsync();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        Assert.DoesNotContain(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(jwtToken.Claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public async Task Hub_Connection_Enforces_Authorization()
    {
        // 1. Verify Unauthenticated uses are Rejected (or at least cannot call authorized methods)
        // Note: SignalR connection might succeed but invocation fails, OR connection fails depending on config.
        // Our AuthHub is unshielded, but AccountHub is [Authorize].
        
        var unauthenticatedConnection = new HubConnectionBuilder()
            .WithUrl($"{ApiBaseUrl}/accountHub")
            .Build();

        // Expectation: Connection might fail outright with 401, or succeed but methods fail.
        // Default AspNetCore behaviour for [Authorize] on Hub class is to reject handshake with 401.
        
        await Assert.ThrowsAnyAsync<Exception>(async () => await unauthenticatedConnection.StartAsync());

        // 2. Verify Authenticated user CAN connect
        var (token, _) = await LoginAsUserAsync();
        var authenticatedConnection = CreateHubConnection("/accountHub", token);
        
        try
        {
            await authenticatedConnection.StartAsync();
            Assert.Equal(HubConnectionState.Connected, authenticatedConnection.State);
        }
        finally
        {
            await authenticatedConnection.DisposeAsync();
        }
    }
}
