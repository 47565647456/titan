using Titan.Abstractions.Models;

namespace Titan.Abstractions.Grains;

/// <summary>
/// Grain for managing short-lived, single-use connection tickets.
/// Tickets are used to authenticate WebSocket connections without exposing JWTs in URLs.
/// Each ticket is a separate grain identified by its ticket ID.
/// </summary>
public interface IConnectionTicketGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a new connection ticket for the specified user.
    /// </summary>
    /// <param name="userId">The user ID to associate with this ticket.</param>
    /// <param name="roles">The roles to include in the ticket.</param>
    /// <param name="lifetime">How long the ticket is valid. Defaults to 30 seconds.</param>
    /// <returns>The created ticket.</returns>
    Task<ConnectionTicket> CreateTicketAsync(Guid userId, string[] roles, TimeSpan? lifetime = null);

    /// <summary>
    /// Validates and consumes a ticket. Returns null if the ticket is invalid,
    /// expired, or already consumed. Tickets are single-use.
    /// </summary>
    /// <returns>The ticket if valid, null otherwise.</returns>
    Task<ConnectionTicket?> ValidateAndConsumeAsync();
}
