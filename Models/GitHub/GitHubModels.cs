using System.Text.Json.Serialization;

namespace GitMC.Models.GitHub;

/// <summary>
///     Request model for GitHub Device Flow authentication
/// </summary>
public class DeviceCodeRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
///     Response model for GitHub Device Flow authentication request
/// </summary>
public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("user_code")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verification_uri")]
    public string VerificationUri { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

/// <summary>
///     Request model for GitHub Device Flow token exchange
/// </summary>
public class DeviceTokenRequest
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("device_code")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = "urn:ietf:params:oauth:grant-type:device_code";
}

/// <summary>
///     Response model for GitHub Device Flow token exchange
/// </summary>
public class DeviceTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
///     GitHub user information model
/// </summary>
public class GitHubUser
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}

/// <summary>
///     Device Flow authentication result
/// </summary>
public class GitHubAuthResult
{
    public bool IsSuccess { get; set; }
    public string? AccessToken { get; set; }
    public GitHubUser? User { get; set; }
    public string? ErrorMessage { get; set; }
}
