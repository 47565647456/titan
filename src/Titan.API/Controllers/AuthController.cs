using System.Security.Claims;
using FluentValidation;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

/// <summary>
/// HTTP authentication endpoints following industry standards.
/// Login/logout use stateless HTTP requests; real-time operations use WebSocket.
/// </summary>
public static class AuthController
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");
        
        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithDescription("Authenticate with a provider token. Returns JWT access token and refresh token.")
            .Produces<LoginResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401);
        
        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .WithDescription("Exchange a valid refresh token for new access and refresh tokens (token rotation).")
            .Produces<RefreshResult>(200)
            .ProducesProblem(401);
        
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithDescription("Revoke the specified refresh token.")
            .Produces(200)
            .ProducesProblem(401);
        
        group.MapGet("/providers", GetProviders)
            .WithName("GetProviders")
            .WithDescription("List available authentication providers (e.g., EOS, Mock).")
            .Produces<IEnumerable<string>>(200);

        group.MapPost("/connection-ticket", GetConnectionTicketAsync)
            .RequireAuthorization()
            .WithName("GetConnectionTicket")
            .WithDescription("Get a short-lived, single-use ticket for WebSocket connection. Use this instead of passing JWTs in query strings.")
            .Produces<ConnectionTicketResponse>(200)
            .ProducesProblem(401);
    }
    
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        IAuthServiceFactory authServiceFactory,
        IClusterClient clusterClient,
        ITokenService tokenService,
        ILogger<LoginRequest> logger)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        logger.LogInformation("Login attempt via provider: {Provider}", request.Provider);
        
        // Validate provider exists
        if (!authServiceFactory.HasProvider(request.Provider))
        {
            var available = string.Join(", ", authServiceFactory.GetProviderNames());
            return Results.BadRequest(new { error = $"Unknown provider '{request.Provider}'. Available: {available}" });
        }
        
        var authService = authServiceFactory.GetService(request.Provider);
        var result = await authService.ValidateTokenAsync(request.Token);
        
        if (!result.Success)
        {
            logger.LogWarning("Login failed for provider {Provider}: {Error}", 
                request.Provider, result.ErrorMessage);
            return Results.Unauthorized();
        }
        
        // Ensure user identity grain exists and link provider
        var identityGrain = clusterClient.GetGrain<IUserIdentityGrain>(result.UserId!.Value);
        await identityGrain.LinkProviderAsync(result.ProviderName!, result.ExternalId!);
        var identity = await identityGrain.GetIdentityAsync();
        
        // Determine roles
        var roles = new List<string> { "User" };
        
        // Admin backdoor for development: "mock:admin:{guid}"
        if (request.Provider.Equals("Mock", StringComparison.OrdinalIgnoreCase) &&
            request.Token.StartsWith("mock:admin:", StringComparison.OrdinalIgnoreCase))
        {
            roles.Add("Admin");
        }
        
        // Generate access token
        var accessToken = tokenService.GenerateAccessToken(result.UserId!.Value, result.ProviderName!, roles);
        var expiresInSeconds = (int)tokenService.AccessTokenExpiration.TotalSeconds;
        
        // Generate refresh token via grain
        var refreshTokenGrain = clusterClient.GetGrain<IRefreshTokenGrain>(result.UserId!.Value);
        var refreshTokenInfo = await refreshTokenGrain.CreateTokenAsync(result.ProviderName!, roles);
        
        logger.LogInformation(
            "Login successful. UserId: {UserId}, Provider: {Provider}, ExternalId: {ExternalId}",
            result.UserId, result.ProviderName, result.ExternalId);
        
        return Results.Ok(new LoginResponse(
            Success: true,
            UserId: result.UserId,
            Provider: result.ProviderName,
            Identity: identity,
            AccessToken: accessToken,
            RefreshToken: refreshTokenInfo.TokenId,
            AccessTokenExpiresInSeconds: expiresInSeconds));
    }
    
    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        IValidator<RefreshRequest> validator,
        IClusterClient clusterClient,
        ITokenService tokenService,
        ILogger<RefreshRequest> logger)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var grain = clusterClient.GetGrain<IRefreshTokenGrain>(request.UserId);
        var tokenInfo = await grain.ConsumeTokenAsync(request.RefreshToken);
        
        if (tokenInfo == null)
        {
            logger.LogWarning("Refresh token invalid or expired for user {UserId}", request.UserId);
            return Results.Unauthorized();
        }
        
        // Generate new access token with stored roles
        var accessToken = tokenService.GenerateAccessToken(request.UserId, tokenInfo.Provider, tokenInfo.Roles);
        var expiresInSeconds = (int)tokenService.AccessTokenExpiration.TotalSeconds;
        
        // Generate new refresh token (rotation)
        var newRefreshTokenInfo = await grain.CreateTokenAsync(tokenInfo.Provider, tokenInfo.Roles);
        
        logger.LogDebug("Token refreshed for user {UserId}", request.UserId);
        
        return Results.Ok(new RefreshResult(accessToken, newRefreshTokenInfo.TokenId, expiresInSeconds));
    }
    
    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        IValidator<LogoutRequest> validator,
        ClaimsPrincipal user,
        IClusterClient clusterClient,
        ILogger<LogoutRequest> logger)
    {
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }
        
        var grain = clusterClient.GetGrain<IRefreshTokenGrain>(userId);
        await grain.RevokeTokenAsync(request.RefreshToken);
        
        logger.LogInformation("User {UserId} logged out, refresh token revoked", userId);
        
        return Results.Ok();
    }
    
    private static async Task<IResult> GetConnectionTicketAsync(
        ClaimsPrincipal user,
        IClusterClient clusterClient,
        ILogger<ConnectionTicketResponse> logger)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        // Extract roles from claims
        var roles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();

        // Generate a unique ticket ID
        var ticketId = Guid.NewGuid().ToString("N");

        // Create ticket via grain (30 second lifetime)
        var ticketGrain = clusterClient.GetGrain<IConnectionTicketGrain>(ticketId);
        await ticketGrain.CreateTicketAsync(userId, roles, TimeSpan.FromSeconds(30));

        logger.LogDebug("Connection ticket created for user {UserId}: {TicketId}", userId, ticketId);

        return Results.Ok(new ConnectionTicketResponse(ticketId));
    }
    
    private static IResult GetProviders(IAuthServiceFactory factory)
    {
        return Results.Ok(factory.GetProviderNames());
    }
}

// Request/Response DTOs for HTTP endpoints

/// <summary>
/// Login request with provider token.
/// </summary>
/// <param name="Token">The authentication token from the provider (e.g., EOS ID token, or "mock:{guid}").</param>
/// <param name="Provider">The provider name. Default: "EOS". Use "Mock" for development.</param>
public record LoginRequest(string Token, string Provider = "EOS");

/// <summary>
/// Refresh token request.
/// </summary>
/// <param name="RefreshToken">The refresh token from a previous login or refresh.</param>
/// <param name="UserId">The user ID associated with the refresh token.</param>
public record RefreshRequest(string RefreshToken, Guid UserId);

/// <summary>
/// Logout request.
/// </summary>
/// <param name="RefreshToken">The refresh token to revoke.</param>
public record LogoutRequest(string RefreshToken);

/// <summary>
/// Login response with tokens and user info.
/// </summary>
public record LoginResponse(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? AccessToken,
    string? RefreshToken,
    int? AccessTokenExpiresInSeconds);

/// <summary>
/// Connection ticket response for WebSocket authentication.
/// </summary>
/// <param name="Ticket">The short-lived, single-use ticket ID to use in WebSocket query string.</param>
public record ConnectionTicketResponse(string Ticket);
