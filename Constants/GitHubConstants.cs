namespace GitMC.Constants;

/// <summary>
///     GitHub Apps configuration constants for authentication
/// </summary>
internal static class GitHubConstants
{
    // GitHub API URLs
    public const string ApiBaseUrl = "https://api.github.com";
    public const string DeviceCodeUrl = "https://github.com/login/device/code";
    public const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    public const string DeviceActivationUrl = "https://github.com/login/device";

    // GitHub App Configuration
    public const string ClientId = "Iv23liYD4sF978bFc6y9";

    // Device Flow Configuration
    public const int PollingIntervalSeconds = 5;
    public const int DeviceFlowTimeoutSeconds = 900; // 15 minutes

    // Scopes required for the application
    public const string RequiredScopes = "repo user:email";

    // HTTP Headers
    public const string UserAgent = "GitMC/1.0";
    public const string AcceptHeader = "application/vnd.github+json";
}
