using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Titan.API.Config;

namespace Titan.API.Services.Auth;

/// <summary>
/// EOS Connect authentication service.
/// Validates ID Tokens (JWTs) from Epic Online Services using JWKS public keys.
/// 
/// Flow:
/// 1. Game client authenticates via EOS Connect SDK
/// 2. Client receives ID Token containing Product User ID (PUID)
/// 3. Client sends ID Token to Titan
/// 4. This service validates the JWT signature using Epic's JWKS endpoint
/// 5. Extracts PUID from 'sub' claim and maps to internal UserId
/// </summary>
public class EosConnectService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly EosOptions _options;
    private readonly ILogger<EosConnectService> _logger;
    
    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksCacheExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _jwksLock = new(1, 1);

    public string ProviderName => "EOS";

    public EosConnectService(
        HttpClient httpClient,
        IOptions<EosOptions> options,
        ILogger<EosConnectService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return AuthResult.Failed("ID Token is empty.");

        try
        {
            // Fetch JWKS keys (cached)
            var jwks = await GetJwksAsync();
            if (jwks == null)
                return AuthResult.Failed("Failed to retrieve EOS JWKS keys.");

            // Build token validation parameters
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                
                // EOS Connect issuer starts with api.epicgames.dev
                IssuerValidator = (issuer, _, _) =>
                {
                    if (issuer.StartsWith(_options.IssuerPrefix, StringComparison.OrdinalIgnoreCase))
                        return issuer;
                    throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");
                },
                
                // Audience must match our Client ID
                ValidAudience = _options.ClientId,
                
                // Use JWKS keys for signature validation
                IssuerSigningKeys = jwks.GetSigningKeys(),
                
                // Allow some clock skew
                ClockSkew = _options.ClockSkew,
                
                // Ensure algorithm is specified and not 'none'
                RequireSignedTokens = true
            };

            var handler = new JwtSecurityTokenHandler();
            
            // Validate the token
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            
            if (validatedToken is not JwtSecurityToken jwtToken)
                return AuthResult.Failed("Invalid token format.");

            // Verify algorithm is not 'none' (defense in depth)
            if (string.IsNullOrEmpty(jwtToken.Header.Alg) || 
                jwtToken.Header.Alg.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return AuthResult.Failed("Token algorithm cannot be 'none'.");
            }

            // Extract Product User ID from 'sub' claim
            var puid = jwtToken.Subject;
            if (string.IsNullOrEmpty(puid))
                return AuthResult.Failed("Token does not contain subject (PUID).");

            // Generate deterministic internal UserId from PUID
            // This ensures the same EOS user always maps to the same Titan UserId
            var userId = GenerateDeterministicGuid(puid);

            _logger.LogInformation(
                "EOS Connect authentication successful. PUID: {Puid}, UserId: {UserId}",
                puid, userId);

            return AuthResult.Authenticated(userId, ProviderName, puid);
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning(ex, "EOS ID Token has expired.");
            return AuthResult.Failed("ID Token has expired.");
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "EOS ID Token signature validation failed.");
            return AuthResult.Failed("ID Token signature is invalid.");
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            _logger.LogWarning(ex, "EOS ID Token audience validation failed.");
            return AuthResult.Failed("ID Token audience does not match Client ID.");
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            _logger.LogWarning(ex, "EOS ID Token issuer validation failed.");
            return AuthResult.Failed("ID Token issuer is invalid.");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "EOS ID Token validation failed.");
            return AuthResult.Failed($"ID Token validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during EOS authentication.");
            return AuthResult.Failed($"Authentication error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches and caches the JWKS from Epic's endpoint.
    /// </summary>
    private async Task<JsonWebKeySet?> GetJwksAsync()
    {
        // Check cache first
        if (_cachedJwks != null && DateTime.UtcNow < _jwksCacheExpiry)
            return _cachedJwks;

        await _jwksLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedJwks != null && DateTime.UtcNow < _jwksCacheExpiry)
                return _cachedJwks;

            _logger.LogDebug("Fetching JWKS from {Uri}", _options.JwksUri);

            var response = await _httpClient.GetAsync(_options.JwksUri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch JWKS. Status: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            _cachedJwks = new JsonWebKeySet(json);
            _jwksCacheExpiry = DateTime.UtcNow.Add(_options.JwksCacheDuration);

            _logger.LogInformation(
                "JWKS cached successfully. Keys: {Count}, Expires: {Expiry}",
                _cachedJwks.Keys.Count, _jwksCacheExpiry);

            return _cachedJwks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching JWKS from {Uri}", _options.JwksUri);
            return null;
        }
        finally
        {
            _jwksLock.Release();
        }
    }

    /// <summary>
    /// Generates a deterministic GUID from the EOS Product User ID.
    /// This ensures the same PUID always maps to the same internal UserId.
    /// </summary>
    private static Guid GenerateDeterministicGuid(string puid)
    {
        // Use SHA256 for better distribution than MD5
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"EOS:PUID:{puid}"));
        
        // Take first 16 bytes for GUID
        return new Guid(hash.AsSpan(0, 16));
    }
}
