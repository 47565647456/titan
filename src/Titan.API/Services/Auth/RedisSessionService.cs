using System.Security.Cryptography;
using MemoryPack;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SessionConfig = Titan.API.Config.SessionOptions;

namespace Titan.API.Services.Auth;

/// <summary>
/// Redis-backed session storage service.
/// </summary>
public class RedisSessionService : ISessionService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly SessionConfig _options;
    private readonly ILogger<RedisSessionService> _logger;

    public RedisSessionService(
        [FromKeyedServices("sessions")] IConnectionMultiplexer redis,
        IOptions<SessionConfig> options,
        ILogger<RedisSessionService> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SessionCreateResult> CreateSessionAsync(
        Guid userId,
        string provider,
        IEnumerable<string> roles,
        bool isAdmin = false)
    {
        var db = _redis.GetDatabase();
        var ticketId = GenerateTicketId();
        var now = DateTimeOffset.UtcNow;
        var lifetime = isAdmin 
            ? TimeSpan.FromMinutes(_options.AdminSessionLifetimeMinutes)
            : TimeSpan.FromMinutes(_options.SessionLifetimeMinutes);

        var ticket = new SessionTicket
        {
            UserId = userId,
            Provider = provider,
            Roles = roles.ToList(),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            LastActivityAt = now,
            IsAdmin = isAdmin
        };

        var sessionKey = GetSessionKey(ticketId);
        var userSessionsKey = GetUserSessionsKey(userId);
        var serialized = MemoryPackSerializer.Serialize(ticket);

        // Store session
        await db.StringSetAsync(sessionKey, serialized, lifetime);

        // Track session in user's session set
        await db.SetAddAsync(userSessionsKey, ticketId);
        await db.KeyExpireAsync(userSessionsKey, lifetime.Add(TimeSpan.FromMinutes(5)));

        // Enforce session limit
        if (_options.MaxSessionsPerUser > 0)
        {
            await EnforceSessionLimitAsync(db, userId, userSessionsKey);
        }

        _logger.LogDebug(
            "Created session {TicketId} for user {UserId}, expires {ExpiresAt}",
            ticketId[..8], userId, ticket.ExpiresAt);

        return new SessionCreateResult(ticketId, ticket.ExpiresAt);
    }

    public async Task<SessionTicket?> ValidateSessionAsync(string ticketId)
    {
        var db = _redis.GetDatabase();
        var sessionKey = GetSessionKey(ticketId);
        
        var data = await db.StringGetAsync(sessionKey);
        if (data.IsNullOrEmpty)
        {
            return null;
        }

        var ticket = MemoryPackSerializer.Deserialize<SessionTicket>(data!);
        if (ticket == null)
        {
            return null;
        }

        // Check if expired (shouldn't happen with Redis TTL, but be safe)
        if (ticket.ExpiresAt < DateTimeOffset.UtcNow)
        {
            await db.KeyDeleteAsync(sessionKey);
            return null;
        }

        // Apply sliding expiration
        var slidingExpiration = TimeSpan.FromMinutes(_options.SlidingExpirationMinutes);
        var newExpiry = DateTimeOffset.UtcNow.Add(slidingExpiration);
        
        // Only extend if new expiry is beyond current
        if (newExpiry > ticket.ExpiresAt)
        {
            // Cap at maximum lifetime from creation (use correct lifetime based on session type)
            // The 2x multiplier allows sliding expiration to extend sessions beyond the initial TTL,
            // up to double the configured lifetime with continuous activity. This is intentional:
            // e.g., 30min session + 30min sliding = 60min max absolute lifetime.
            var maxLifetimeMinutes = ticket.IsAdmin 
                ? _options.AdminSessionLifetimeMinutes 
                : _options.SessionLifetimeMinutes;
            var maxExpiry = ticket.CreatedAt.Add(
                TimeSpan.FromMinutes(maxLifetimeMinutes * 2));
            newExpiry = newExpiry > maxExpiry ? maxExpiry : newExpiry;


            var updatedTicket = ticket with 
            { 
                ExpiresAt = newExpiry, 
                LastActivityAt = DateTimeOffset.UtcNow 
            };
            var serialized = MemoryPackSerializer.Serialize(updatedTicket);
            await db.StringSetAsync(sessionKey, serialized, newExpiry - DateTimeOffset.UtcNow);
            
            return updatedTicket;
        }

        return ticket;
    }

    public async Task<bool> InvalidateSessionAsync(string ticketId)
    {
        var db = _redis.GetDatabase();
        var sessionKey = GetSessionKey(ticketId);
        
        // Get session to find user ID
        var data = await db.StringGetAsync(sessionKey);
        if (data.IsNullOrEmpty)
        {
            return false;
        }

        var ticket = MemoryPackSerializer.Deserialize<SessionTicket>(data!);
        if (ticket != null)
        {
            // Remove from user's session set
            var userSessionsKey = GetUserSessionsKey(ticket.UserId);
            await db.SetRemoveAsync(userSessionsKey, ticketId);
        }

        var deleted = await db.KeyDeleteAsync(sessionKey);
        
        if (deleted)
        {
            _logger.LogDebug("Invalidated session {TicketId}", ticketId[..Math.Min(8, ticketId.Length)]);
        }

        return deleted;
    }

    public async Task<int> InvalidateAllSessionsAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        var userSessionsKey = GetUserSessionsKey(userId);
        
        var sessions = await db.SetMembersAsync(userSessionsKey);
        if (sessions.Length == 0)
        {
            return 0;
        }

        // Batch delete all session keys in single round-trip
        var sessionKeys = sessions
            .Select(s => (RedisKey)GetSessionKey(s.ToString()!))
            .ToArray();
        var deleted = await db.KeyDeleteAsync(sessionKeys);

        // Delete the user sessions set
        await db.KeyDeleteAsync(userSessionsKey);
        
        _logger.LogInformation("Invalidated {Count} sessions for user {UserId}", deleted, userId);
        return (int)deleted;
    }

    private async Task EnforceSessionLimitAsync(IDatabase db, Guid userId, string userSessionsKey)
    {
        var sessions = await db.SetMembersAsync(userSessionsKey);
        if (sessions.Length <= _options.MaxSessionsPerUser)
        {
            return;
        }

        // Build keys array for batch fetch (single round-trip instead of N)
        var ticketIds = sessions.Select(s => s.ToString()!).ToArray();
        var keys = ticketIds.Select(id => (RedisKey)GetSessionKey(id)).ToArray();
        
        // Batch fetch all sessions in single MGET call
        var values = await db.StringGetAsync(keys);
        
        var sessionDetails = new List<(string ticketId, DateTimeOffset createdAt)>();
        var expiredTickets = new List<string>();
        
        for (int i = 0; i < ticketIds.Length; i++)
        {
            var ticketId = ticketIds[i];
            var data = values[i];
            
            if (!data.IsNullOrEmpty)
            {
                var ticket = MemoryPackSerializer.Deserialize<SessionTicket>(data!);
                if (ticket != null)
                {
                    sessionDetails.Add((ticketId, ticket.CreatedAt));
                }
            }
            else
            {
                // Session expired, queue for removal
                expiredTickets.Add(ticketId);
            }
        }
        
        // Batch remove expired sessions
        if (expiredTickets.Count > 0)
        {
            var expiredRedisValues = expiredTickets.Select(t => (RedisValue)t).ToArray();
            await db.SetRemoveAsync(userSessionsKey, expiredRedisValues);
        }

        // Remove oldest sessions to get under limit
        var toRemove = sessionDetails
            .OrderBy(s => s.createdAt)
            .Take(sessionDetails.Count - _options.MaxSessionsPerUser)
            .ToList();

        if (toRemove.Count > 0)
        {
            // Batch delete session keys
            var keysToDelete = toRemove.Select(s => (RedisKey)GetSessionKey(s.ticketId)).ToArray();
            await db.KeyDeleteAsync(keysToDelete);
            
            // Batch remove from user's session set
            var valuesToRemove = toRemove.Select(s => (RedisValue)s.ticketId).ToArray();
            await db.SetRemoveAsync(userSessionsKey, valuesToRemove);
            
            foreach (var (ticketId, _) in toRemove)
            {
                _logger.LogDebug("Evicted old session {TicketId} for user {UserId} due to limit", 
                    ticketId[..8], userId);
            }
        }
    }

    private static string GenerateTicketId()
    {
        // 32 bytes = 256 bits of entropy, URL-safe Base64 encoded
        var bytes = RandomNumberGenerator.GetBytes(32);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private string GetSessionKey(string ticketId)
    {
        return $"{_options.KeyPrefix}:{ticketId}";
    }

    private string GetUserSessionsKey(Guid userId)
    {
        return $"{_options.KeyPrefix}:user:{userId}";
    }

    /// <summary>
    /// Scans Redis for all session keys (excluding user tracking keys).
    /// Note: For very large deployments (100k+ sessions), this loads all keys into memory.
    /// Consider cursor-based pagination if memory becomes a concern at scale.
    /// Note: This uses the first available Redis server. In cluster deployments,
    /// ensure sessions are on a single node or iterate all servers if needed.
    /// </summary>
    private async Task<List<RedisKey>> GetSessionKeysAsync()
    {
        var servers = _redis.GetServers();
        var server = servers.FirstOrDefault();
        if (server == null)
        {
            _logger.LogWarning("No Redis servers available");
            return [];
        }

        var pattern = $"{_options.KeyPrefix}:*";
        var keys = new List<RedisKey>();
        
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            var keyStr = key.ToString()!;
            // Skip user session tracking keys (e.g., session:user:guid)
            if (!keyStr.Contains(":user:"))
            {
                keys.Add(key);
            }
        }
        
        return keys;
    }

    public async Task<SessionListResult> GetAllSessionsAsync(int skip = 0, int take = 50)
    {
        var db = _redis.GetDatabase();
        var allKeys = await GetSessionKeysAsync();
        var totalCount = allKeys.Count;
        
        // Apply pagination
        var pagedKeys = allKeys.Skip(skip).Take(take).ToArray();
        if (pagedKeys.Length == 0)
        {
            return new SessionListResult([], totalCount, skip, take);
        }

        // Batch fetch session data
        var values = await db.StringGetAsync(pagedKeys);
        var sessions = new List<SessionInfo>();

        for (int i = 0; i < pagedKeys.Length; i++)
        {
            var data = values[i];
            if (!data.IsNullOrEmpty)
            {
                var ticket = MemoryPackSerializer.Deserialize<SessionTicket>(data!);
                if (ticket != null)
                {
                    // Extract ticket ID from key: "session:ticketId"
                    var ticketId = pagedKeys[i].ToString()!.Replace($"{_options.KeyPrefix}:", "");
                    sessions.Add(new SessionInfo(
                        TicketId: ticketId,
                        UserId: ticket.UserId,
                        Provider: ticket.Provider,
                        Roles: ticket.Roles,
                        CreatedAt: ticket.CreatedAt,
                        ExpiresAt: ticket.ExpiresAt,
                        LastActivityAt: ticket.LastActivityAt,
                        IsAdmin: ticket.IsAdmin
                    ));
                }
            }
        }

        return new SessionListResult(sessions, totalCount, skip, take);
    }

    public async Task<int> GetSessionCountAsync()
    {
        var keys = await GetSessionKeysAsync();
        return keys.Count;
    }

    public async Task<IReadOnlyList<SessionInfo>> GetUserSessionsAsync(Guid userId)
    {
        var db = _redis.GetDatabase();
        var userSessionsKey = GetUserSessionsKey(userId);
        
        var ticketIds = await db.SetMembersAsync(userSessionsKey);
        if (ticketIds.Length == 0)
        {
            return [];
        }

        // Batch fetch all user sessions
        var keys = ticketIds.Select(id => (RedisKey)GetSessionKey(id.ToString()!)).ToArray();
        var values = await db.StringGetAsync(keys);
        
        var sessions = new List<SessionInfo>();
        for (int i = 0; i < ticketIds.Length; i++)
        {
            var data = values[i];
            if (!data.IsNullOrEmpty)
            {
                var ticket = MemoryPackSerializer.Deserialize<SessionTicket>(data!);
                if (ticket != null)
                {
                    var ticketId = ticketIds[i].ToString()!;
                    sessions.Add(new SessionInfo(
                        TicketId: ticketId,  // Return full ticket ID for invalidation
                        UserId: ticket.UserId,
                        Provider: ticket.Provider,
                        Roles: ticket.Roles,
                        CreatedAt: ticket.CreatedAt,
                        ExpiresAt: ticket.ExpiresAt,
                        LastActivityAt: ticket.LastActivityAt,
                        IsAdmin: ticket.IsAdmin
                    ));
                }
            }
        }

        return sessions;
    }
}
