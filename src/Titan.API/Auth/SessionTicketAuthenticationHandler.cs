using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Titan.API.Services.Auth;

namespace Titan.API.Auth;

/// <summary>
/// Configuration for session ticket authentication.
/// </summary>
public class SessionTicketAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Custom authentication handler that validates session tickets from Redis.
/// </summary>
public class SessionTicketAuthenticationHandler : AuthenticationHandler<SessionTicketAuthenticationOptions>
{
    private readonly ISessionService _sessionService;

    public SessionTicketAuthenticationHandler(
        IOptionsMonitor<SessionTicketAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISessionService sessionService)
        : base(options, logger, encoder)
    {
        _sessionService = sessionService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Extract ticket from Authorization header or query string (SignalR)
        string? ticketId = null;

        // Check Authorization: Bearer <ticket>
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            ticketId = authHeader["Bearer ".Length..].Trim();
        }

        // Check query string for SignalR (access_token parameter)
        if (string.IsNullOrEmpty(ticketId))
        {
            ticketId = Request.Query["access_token"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(ticketId))
        {
            return AuthenticateResult.NoResult();
        }

        // Log session validation attempt
        Logger.LogDebug("Validating session ticket: {TicketId}", ticketId[..Math.Min(8, ticketId.Length)]);

        // Validate session
        var session = await _sessionService.ValidateSessionAsync(ticketId);
        if (session == null)
        {
            Logger.LogWarning("Session validation failed for ticket: {TicketId}", ticketId[..Math.Min(8, ticketId.Length)]);
            return AuthenticateResult.Fail("Invalid or expired session");
        }
        
        Logger.LogDebug("Session validated for user: {UserId}", session.UserId);

        // Build claims principal
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new("provider", session.Provider),
            new("session_id", ticketId)
        };

        foreach (var role in session.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
