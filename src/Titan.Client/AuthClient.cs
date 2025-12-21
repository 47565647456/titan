using System.Net.Http.Json;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;

namespace Titan.Client;

/// <summary>
/// HTTP client for authentication operations.
/// Implements IAuthClient for login/logout via HTTP.
/// Session-based authentication.
/// </summary>
internal sealed class AuthClient : IAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly TitanClient _parent;

    public AuthClient(HttpClient httpClient, TitanClient parent)
    {
        _httpClient = httpClient;
        _parent = parent;
    }

    public async Task<LoginResponse> LoginAsync(string token, string provider = "EOS", CancellationToken ct = default)
    {
        var request = new { token, provider };
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(ct)
            ?? throw new InvalidOperationException("Invalid login response");

        if (result.Success && result.SessionId != null && result.UserId.HasValue && result.ExpiresAt.HasValue)
        {
            _parent.SetSession(result.SessionId, result.UserId.Value, result.ExpiresAt.Value);
        }

        return result;
    }

    public async Task<bool> LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/auth/logout", null, ct);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<LogoutResponse>(ct);
            return result?.SessionInvalidated ?? false;
        }
        finally
        {
            // Always clear local session, even if server request fails
            _parent.ClearSession();
        }
    }

    public async Task<int> LogoutAllAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/auth/logout-all", null, ct);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<LogoutAllResult>(ct);
            return result?.SessionsInvalidated ?? 0;
        }
        finally
        {
            // Always clear local session, even if server request fails
            _parent.ClearSession();
        }
    }

    public async Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<string>>("/api/auth/providers", ct)
            ?? Array.Empty<string>();
    }

    private record LogoutAllResult(int SessionsInvalidated);
}
