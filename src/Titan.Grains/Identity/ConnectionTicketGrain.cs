using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

/// <summary>
/// Grain implementation for short-lived, single-use connection tickets.
/// Tickets are used for WebSocket authentication to avoid exposing JWTs in URLs.
/// 
/// Design:
/// - Each ticket is a separate grain (keyed by ticket ID)
/// - Tickets are in-memory only (no persistence needed)
/// - Tickets auto-deactivate after expiry or consumption
/// - Handshake window: allows multiple validations within 10 seconds of first use
///   (SignalR makes multiple requests: negotiate + websocket connection)
/// </summary>
public class ConnectionTicketGrain : Grain, IConnectionTicketGrain
{
    private ConnectionTicket? _ticket;
    private IGrainTimer? _expiryTimer;
    private DateTimeOffset? _firstUsedAt;

    /// <summary>
    /// Default ticket lifetime if not specified.
    /// </summary>
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Handshake window - allows ticket reuse during SignalR connection handshake.
    /// SignalR makes multiple requests (negotiate + websocket), so we need a short window.
    /// </summary>
    private static readonly TimeSpan HandshakeWindow = TimeSpan.FromSeconds(10);

    public Task<ConnectionTicket> CreateTicketAsync(Guid userId, string[] roles, TimeSpan? lifetime = null)
    {
        var expiryDuration = lifetime ?? DefaultLifetime;

        _ticket = new ConnectionTicket
        {
            TicketId = this.GetPrimaryKeyString(),
            UserId = userId,
            Roles = roles,
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiryDuration),
            IsConsumed = false
        };

        // Schedule grain deactivation after expiry using the new RegisterGrainTimer API
        // This ensures tickets don't linger in memory
        _expiryTimer = this.RegisterGrainTimer(
            static (state, cancellationToken) =>
            {
                state.DeactivateOnIdle();
                return Task.CompletedTask;
            },
            this,
            new GrainTimerCreationOptions
            {
                DueTime = expiryDuration,
                Period = Timeout.InfiniteTimeSpan, // Don't repeat
                Interleave = true
            });

        return Task.FromResult(_ticket);
    }

    public Task<ConnectionTicket?> ValidateAndConsumeAsync()
    {
        // No ticket created for this grain
        if (_ticket == null)
        {
            return Task.FromResult<ConnectionTicket?>(null);
        }

        // Expired
        if (_ticket.ExpiresAt < DateTimeOffset.UtcNow)
        {
            DeactivateOnIdle();
            return Task.FromResult<ConnectionTicket?>(null);
        }

        // Check if within handshake window (allows SignalR negotiate + websocket requests)
        if (_firstUsedAt.HasValue)
        {
            // Already used - check if within handshake window
            if (DateTimeOffset.UtcNow - _firstUsedAt.Value > HandshakeWindow)
            {
                // Handshake window expired - ticket fully consumed
                DeactivateOnIdle();
                return Task.FromResult<ConnectionTicket?>(null);
            }
            // Within handshake window - allow reuse
            return Task.FromResult<ConnectionTicket?>(_ticket);
        }

        // First use - start the handshake window
        _firstUsedAt = DateTimeOffset.UtcNow;

        // Schedule deactivation after handshake window
        this.RegisterGrainTimer(
            static (state, cancellationToken) =>
            {
                state.DeactivateOnIdle();
                return Task.CompletedTask;
            },
            this,
            new GrainTimerCreationOptions
            {
                DueTime = HandshakeWindow,
                Period = Timeout.InfiniteTimeSpan,
                Interleave = true
            });

        return Task.FromResult<ConnectionTicket?>(_ticket);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _expiryTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}

