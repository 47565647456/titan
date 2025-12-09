using System.Net.Http.Headers;
using System.Text.Json;

namespace Titan.API.Services.Auth;

/// <summary>
/// EOS (Epic Online Services) authentication service.
/// Validates EOS Connect tokens against Epic's API.
/// </summary>
public class EosAuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _deploymentId;

    public string ProviderName => "EOS";

    public EosAuthService(HttpClient httpClient, string clientId, string clientSecret, string deploymentId)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _deploymentId = deploymentId;
    }

    public async Task<AuthResult> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return AuthResult.Failed("Token is empty.");

        try
        {
            // EOS Connect token validation endpoint
            // In production, you would call the EOS API to validate the token
            // For now, this is a placeholder that shows the structure
            
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.epicgames.dev/auth/v1/oauth/introspect");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", 
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_clientId}:{_clientSecret}")));
            
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["token_type_hint"] = "access_token"
            });

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
                return AuthResult.Failed($"EOS validation failed: {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            
            if (!json.RootElement.TryGetProperty("active", out var activeElement) || !activeElement.GetBoolean())
                return AuthResult.Failed("Token is not active.");

            if (!json.RootElement.TryGetProperty("sub", out var subElement))
                return AuthResult.Failed("Token does not contain subject.");

            var externalId = subElement.GetString()!;
            
            // Generate or lookup internal UserId based on externalId
            // For now, we create a deterministic GUID from the external ID
            var userId = GenerateDeterministicGuid(externalId);

            return AuthResult.Authenticated(userId, ProviderName, externalId);
        }
        catch (Exception ex)
        {
            return AuthResult.Failed($"EOS validation error: {ex.Message}");
        }
    }

    private static Guid GenerateDeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"EOS:{input}"));
        return new Guid(hash);
    }
}
