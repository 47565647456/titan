using System.Net.Http.Json;
using Titan.Abstractions.Contracts;
using Titan.Abstractions.Models;

namespace Titan.Client;

/// <summary>
/// HTTP client for authentication operations.
/// Implements IAuthClient for login/logout via HTTP.
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

        if (result.Success && result.AccessToken != null && result.UserId.HasValue)
        {
            _parent.SetAuthState(result.AccessToken, result.UserId.Value);
        }

        return result;
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken, Guid userId, CancellationToken ct = default)
    {
        var request = new { refreshToken, userId };
        var response = await _httpClient.PostAsJsonAsync("/api/auth/refresh", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RefreshResult>(ct)
            ?? throw new InvalidOperationException("Invalid refresh response");

        _parent.SetAuthState(result.AccessToken, userId);

        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var request = new { refreshToken };
        var response = await _httpClient.PostAsJsonAsync("/api/auth/logout", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> GetProvidersAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<string>>("/api/auth/providers", ct)
            ?? Array.Empty<string>();
    }
}
