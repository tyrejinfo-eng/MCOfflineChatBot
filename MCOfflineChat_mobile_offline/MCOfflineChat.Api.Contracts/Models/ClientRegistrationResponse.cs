using System.Text.Json.Serialization;

namespace MCOfflineChat.Api.Contracts.Models;

public class ClientRegistrationResponse
{
    [JsonPropertyName("clientId")]
    public Guid ClientId { get; set; }

    /// <summary>
    /// JWT authentication token. Use this as the Bearer token for all subsequent API calls.
    /// </summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Kept for backward compatibility. Contains the same value as Token.
    /// New clients should use the Token field instead.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("serverVersion")]
    public string ServerVersion { get; set; } = string.Empty;

    /// <summary>
    /// v1.1.72: Opaque refresh token for obtaining new access tokens without re-authenticating.
    /// One-time use — each refresh returns a new refresh token in the same token family.
    /// </summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// v1.1.72: Access token lifetime in seconds (default 900 = 15 minutes).
    /// </summary>
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; } = 900;
}
