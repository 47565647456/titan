using System.Security.Claims;
using FluentValidation;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;
using Titan.API.Services.Auth;

namespace Titan.API.Controllers;

/// <summary>
/// HTTP authentication endpoints following industry standards.
/// Login/logout use HTTP requests; real-time operations use WebSocket.
/// Session-based authentication with Redis-backed tickets.
/// </summary>
public static class AuthController
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");
        
        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithDescription("Authenticate with a provider token. Returns session ticket.")
            .Produces<LoginResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401);
        
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithDescription("Invalidate the current session.")
            .Produces(200)
            .ProducesProblem(401);
        
        group.MapPost("/logout-all", LogoutAllAsync)
            .RequireAuthorization()
            .WithName("LogoutAll")
            .WithDescription("Invalidate all sessions for the current user.")
            .Produces<LogoutAllResponse>(200)
            .ProducesProblem(401);
        
        group.MapGet("/providers", GetProviders)
            .WithName("GetProviders")
            .WithDescription("List available authentication providers (e.g., EOS, Mock).")
            .Produces<IEnumerable<string>>(200);
    }
    
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        IAuthServiceFactory authServiceFactory,
        IClusterClient clusterClient,
        ISessionService sessionService,
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
        
        // Create session
        var session = await sessionService.CreateSessionAsync(
            result.UserId!.Value, 
            result.ProviderName!, 
            roles);
        
        logger.LogInformation(
            "Login successful. UserId: {UserId}, Provider: {Provider}, SessionExpires: {ExpiresAt}",
            result.UserId, result.ProviderName, session.ExpiresAt);
        
        return Results.Ok(new LoginResponse(
            Success: true,
            UserId: result.UserId,
            Provider: result.ProviderName,
            Identity: identity,
            SessionId: session.TicketId,
            ExpiresAt: session.ExpiresAt));
    }
    
    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user,
        ISessionService sessionService,
        ILogger<LogoutRequest> logger)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionId = user.FindFirstValue("session_id");
        
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }
        
        if (!string.IsNullOrEmpty(sessionId))
        {
            await sessionService.InvalidateSessionAsync(sessionId);
        }
        
        logger.LogInformation("User {UserId} logged out, session invalidated", userId);
        
        return Results.Ok(new { success = true });
    }
    
    private static async Task<IResult> LogoutAllAsync(
        ClaimsPrincipal user,
        ISessionService sessionService,
        ILogger<LogoutRequest> logger)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }
        
        var count = await sessionService.InvalidateAllSessionsAsync(userId);
        
        logger.LogInformation("User {UserId} logged out of all {Count} sessions", userId, count);
        
        return Results.Ok(new LogoutAllResponse(count));
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
/// Logout request (kept for validator compatibility).
/// </summary>
public record LogoutRequest();

/// <summary>
/// Login response with session ticket and user info.
/// </summary>
public record LoginResponse(
    bool Success,
    Guid? UserId,
    string? Provider,
    UserIdentity? Identity,
    string? SessionId,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Logout all response.
/// </summary>
public record LogoutAllResponse(int SessionsInvalidated);
