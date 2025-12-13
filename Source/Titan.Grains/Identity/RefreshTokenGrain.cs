using System.Security.Cryptography;
using MemoryPack;
using Microsoft.Extensions.Configuration;
using Orleans.Runtime;
using Titan.Abstractions.Grains;
using Titan.Abstractions.Models;

namespace Titan.Grains.Identity;

[GenerateSerializer]
[MemoryPackable]
public partial class RefreshTokenGrainState
{
    [Id(0), MemoryPackOrder(0)] 
    public Dictionary<string, RefreshTokenInfo> ActiveTokens { get; set; } = new();
}

/// <summary>
/// Per-user grain for managing refresh tokens.
/// Supports token creation, rotation (consume), and revocation.
/// </summary>
public class RefreshTokenGrain : Grain, IRefreshTokenGrain
{
    private readonly IPersistentState<RefreshTokenGrainState> _state;
    private readonly TimeSpan _tokenLifetime;

    public RefreshTokenGrain(
        [PersistentState("refreshTokens", "OrleansStorage")] 
        IPersistentState<RefreshTokenGrainState> state,
        IConfiguration configuration)
    {
        _state = state;
        _tokenLifetime = TimeSpan.FromHours(
            configuration.GetValue("Jwt:RefreshTokenExpirationHours", 24));
    }

    public async Task<RefreshTokenInfo> CreateTokenAsync(string provider, IReadOnlyList<string> roles)
    {
        // Cleanup expired tokens on each create
        CleanupExpiredTokens();

        // Generate cryptographically secure token ID
        var tokenId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        
        var info = new RefreshTokenInfo
        {
            TokenId = tokenId,
            Provider = provider,
            Roles = roles,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_tokenLifetime)
        };

        _state.State.ActiveTokens[tokenId] = info;
        await _state.WriteStateAsync();
        
        return info;
    }

    public async Task<RefreshTokenInfo?> ConsumeTokenAsync(string tokenId)
    {
        CleanupExpiredTokens();

        if (!_state.State.ActiveTokens.TryGetValue(tokenId, out var info))
            return null;

        // Check if expired (shouldn't happen after cleanup, but be safe)
        if (info.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _state.State.ActiveTokens.Remove(tokenId);
            await _state.WriteStateAsync();
            return null;
        }

        // Token rotation: remove the consumed token
        _state.State.ActiveTokens.Remove(tokenId);
        await _state.WriteStateAsync();
        
        return info;
    }

    public async Task RevokeTokenAsync(string tokenId)
    {
        if (_state.State.ActiveTokens.Remove(tokenId))
        {
            await _state.WriteStateAsync();
        }
    }

    public async Task RevokeAllTokensAsync()
    {
        if (_state.State.ActiveTokens.Count > 0)
        {
            _state.State.ActiveTokens.Clear();
            await _state.WriteStateAsync();
        }
    }

    public Task<int> GetActiveTokenCountAsync()
    {
        CleanupExpiredTokens();
        return Task.FromResult(_state.State.ActiveTokens.Count);
    }

    private void CleanupExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _state.State.ActiveTokens
            .Where(kvp => kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in expired)
        {
            _state.State.ActiveTokens.Remove(key);
        }
    }
}
