using GitMC.Models.GitHub;

namespace GitMC.Services;

/// <summary>
///     Service interface for GitHub Apps authentication and API integration
/// </summary>
public interface IGitHubAppsService
{
    /// <summary>
    ///     Initiates the GitHub Device Flow authentication process
    /// </summary>
    /// <returns>Device code response containing user code and verification URI</returns>
    Task<DeviceCodeResponse?> StartDeviceFlowAsync();

    /// <summary>
    ///     Polls GitHub for device flow completion and retrieves access token
    /// </summary>
    /// <param name="deviceCode">Device code from StartDeviceFlowAsync</param>
    /// <param name="cancellationToken">Cancellation token to stop polling</param>
    /// <returns>Authentication result containing access token and user info</returns>
    Task<GitHubAuthResult> PollDeviceFlowAsync(string deviceCode, CancellationToken cancellationToken);

    /// <summary>
    ///     Completes the full Device Flow authentication process
    /// </summary>
    /// <param name="progress">Progress callback for UI updates</param>
    /// <param name="cancellationToken">Cancellation token to stop the process</param>
    /// <returns>Authentication result</returns>
    Task<GitHubAuthResult> AuthenticateWithDeviceFlowAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current authenticated user information
    /// </summary>
    /// <param name="accessToken">GitHub access token</param>
    /// <returns>User information</returns>
    Task<GitHubUser?> GetUserAsync(string accessToken);

    /// <summary>
    ///     Tests if an access token is valid
    /// </summary>
    /// <param name="accessToken">Access token to test</param>
    /// <returns>True if token is valid</returns>
    Task<bool> ValidateTokenAsync(string accessToken);

    /// <summary>
    ///     Checks if a GitHub access token is expired based on timestamp.
    ///     GitHub Apps tokens typically expire after 8 hours.
    /// </summary>
    /// <param name="tokenTimestamp">When the token was issued</param>
    /// <returns>True if token is expired</returns>
    bool IsTokenExpired(DateTime tokenTimestamp);

    /// <summary>
    ///     Validates both token validity and expiration status.
    /// </summary>
    /// <param name="accessToken">Access token to validate</param>
    /// <param name="tokenTimestamp">When the token was issued</param>
    /// <returns>Tuple containing validity status, expiration status, and error message</returns>
    Task<(bool IsValid, bool IsExpired, string? ErrorMessage)> ValidateTokenStateAsync(string? accessToken,
        DateTime tokenTimestamp);

    /// <summary>
    ///     Creates a new repository for the authenticated user
    /// </summary>
    /// <param name="accessToken">GitHub access token</param>
    /// <param name="repositoryName">Name of the repository to create</param>
    /// <param name="isPrivate">Whether the repository should be private</param>
    /// <param name="description">Repository description</param>
    /// <returns>True if repository was created successfully</returns>
    Task<bool> CreateRepositoryAsync(string accessToken, string repositoryName, bool isPrivate = true,
        string? description = null);

    /// <summary>
    ///     Checks if a repository exists for the authenticated user
    /// </summary>
    /// <param name="accessToken">GitHub access token</param>
    /// <param name="repositoryName">Name of the repository to check</param>
    /// <returns>True if repository exists</returns>
    Task<bool> CheckRepositoryExistsAsync(string accessToken, string repositoryName);

    /// <summary>
    ///     Gets the authenticated user's repositories
    /// </summary>
    /// <param name="accessToken">GitHub access token</param>
    /// <param name="includePrivate">Whether to include private repositories</param>
    /// <returns>Array of user repositories</returns>
    Task<GitHubRepository[]> GetUserRepositoriesAsync(string accessToken, bool includePrivate = true);

    /// <summary>
    ///     Gets detailed information about a repository
    /// </summary>
    /// <param name="accessToken">GitHub access token</param>
    /// <param name="owner">Repository owner</param>
    /// <param name="repositoryName">Repository name</param>
    /// <returns>Repository information or null if not found</returns>
    Task<GitHubRepository?> GetRepositoryInfoAsync(string accessToken, string owner, string repositoryName);
}