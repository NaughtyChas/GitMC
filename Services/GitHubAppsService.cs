using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GitMC.Constants;
using GitMC.Models.GitHub;

namespace GitMC.Services;

/// <summary>
///     GitHub Apps service implementation for authentication and API integration
/// </summary>
public class GitHubAppsService : IGitHubAppsService, IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public GitHubAppsService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubConstants.UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(GitHubConstants.AcceptHeader));
    }

    public async Task<DeviceCodeResponse?> StartDeviceFlowAsync()
    {
        try
        {
            var request = new DeviceCodeRequest
            {
                ClientId = GitHubConstants.ClientId,
                Scope = GitHubConstants.RequiredScopes
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(GitHubConstants.DeviceCodeUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DeviceCodeResponse>(responseJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GitHubAuthResult> PollDeviceFlowAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var tokenRequest = new DeviceTokenRequest
        {
            ClientId = GitHubConstants.ClientId,
            DeviceCode = deviceCode
        };

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(GitHubConstants.DeviceFlowTimeoutSeconds);

        while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var json = JsonSerializer.Serialize(tokenRequest, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(GitHubConstants.AccessTokenUrl, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<DeviceTokenResponse>(responseJson, JsonOptions);

                if (tokenResponse?.AccessToken != null)
                {
                    // Success! Get user information
                    var user = await GetUserAsync(tokenResponse.AccessToken);
                    return new GitHubAuthResult
                    {
                        IsSuccess = true,
                        AccessToken = tokenResponse.AccessToken,
                        User = user
                    };
                }

                if (tokenResponse?.Error == "authorization_pending")
                {
                    // User hasn't authorized yet, continue polling
                    await Task.Delay(TimeSpan.FromSeconds(GitHubConstants.PollingIntervalSeconds), cancellationToken);
                    continue;
                }

                if (tokenResponse?.Error == "slow_down")
                {
                    // GitHub is asking us to slow down, increase interval
                    await Task.Delay(TimeSpan.FromSeconds(GitHubConstants.PollingIntervalSeconds + 5), cancellationToken);
                    continue;
                }

                // Other errors (expired_token, unsupported_grant_type, etc.)
                return new GitHubAuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? "Authentication failed"
                };
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                return new GitHubAuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Network error: {ex.Message}"
                };
            }
        }

        return new GitHubAuthResult
        {
            IsSuccess = false,
            ErrorMessage = cancellationToken.IsCancellationRequested ? "Operation cancelled" : "Authentication timeout"
        };
    }

    public async Task<GitHubAuthResult> AuthenticateWithDeviceFlowAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report("Starting GitHub authentication...");

            // Step 1: Get device code
            var deviceCodeResponse = await StartDeviceFlowAsync();
            if (deviceCodeResponse == null)
            {
                return new GitHubAuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Failed to start GitHub authentication process"
                };
            }

            progress?.Report($"Please visit {deviceCodeResponse.VerificationUri} and enter code: {deviceCodeResponse.UserCode}");

            // Step 2: Poll for completion
            var result = await PollDeviceFlowAsync(deviceCodeResponse.DeviceCode, cancellationToken);

            if (result.IsSuccess)
            {
                progress?.Report($"Successfully authenticated as {result.User?.Login ?? "unknown user"}");
            }
            else
            {
                progress?.Report($"Authentication failed: {result.ErrorMessage}");
            }

            return result;
        }
        catch (Exception ex)
        {
            return new GitHubAuthResult
            {
                IsSuccess = false,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    public async Task<GitHubUser?> GetUserAsync(string accessToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubConstants.ApiBaseUrl}/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitHubUser>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ValidateTokenAsync(string accessToken)
    {
        try
        {
            var user = await GetUserAsync(accessToken);
            return user != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a GitHub access token is expired based on timestamp.
    /// GitHub Apps tokens typically expire after 8 hours.
    /// </summary>
    public bool IsTokenExpired(DateTime tokenTimestamp)
    {
        if (tokenTimestamp == DateTime.MinValue)
            return true; // No timestamp means token is invalid

        var tokenAge = DateTime.UtcNow - tokenTimestamp;
        return tokenAge.TotalHours >= 8; // GitHub Apps tokens expire after ~8 hours
    }

    /// <summary>
    /// Validates both token validity and expiration status.
    /// </summary>
    public async Task<(bool IsValid, bool IsExpired, string? ErrorMessage)> ValidateTokenStateAsync(string? accessToken, DateTime tokenTimestamp)
    {
        if (string.IsNullOrEmpty(accessToken))
            return (false, false, "No access token found");

        bool isExpired = IsTokenExpired(tokenTimestamp);
        if (isExpired)
            return (false, true, "Token has expired (older than 8 hours)");

        bool isValid = await ValidateTokenAsync(accessToken);
        if (!isValid)
            return (false, false, "Token is invalid or revoked");

        return (true, false, null);
    }

    public async Task<bool> CreateRepositoryAsync(string accessToken, string repositoryName, bool isPrivate = true, string? description = null)
    {
        try
        {
            var repository = new
            {
                name = repositoryName,
                description = description ?? $"Minecraft save repository managed by GitMC",
                @private = isPrivate,
                auto_init = true,
                gitignore_template = "Global/Archives"
            };

            var json = JsonSerializer.Serialize(repository, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{GitHubConstants.ApiBaseUrl}/user/repos")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Handle specific HTTP error codes
            string errorContent = await response.Content.ReadAsStringAsync();
            string errorMessage = $"GitHub API error: {response.StatusCode}";

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException("GitHub access token is invalid or expired.");
                case System.Net.HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException("Insufficient permissions to create repositories. Please check your GitHub account settings.");
                case System.Net.HttpStatusCode.UnprocessableEntity:
                    // Usually means repository name already exists or is invalid
                    throw new InvalidOperationException($"Repository '{repositoryName}' already exists or the name is invalid.");
                case System.Net.HttpStatusCode.TooManyRequests:
                    throw new InvalidOperationException("GitHub API rate limit exceeded. Please try again later.");
                default:
                    throw new HttpRequestException($"{errorMessage}. Response: {errorContent}");
            }
        }
        catch (TaskCanceledException)
        {
            throw new TaskCanceledException("Repository creation request timed out.");
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw authorization exceptions
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw validation exceptions
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error creating repository: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Checks if a repository exists for the authenticated user
    /// </summary>
    public async Task<bool> CheckRepositoryExistsAsync(string accessToken, string repositoryName)
    {
        try
        {
            var user = await GetUserAsync(accessToken);
            if (user == null)
            {
                throw new UnauthorizedAccessException("Unable to get user information. Access token may be invalid.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubConstants.ApiBaseUrl}/repos/{user.Login}/{repositoryName}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false; // Repository doesn't exist, which is what we want to know
            }

            // Handle other error codes
            string errorContent = await response.Content.ReadAsStringAsync();
            string errorMessage = $"GitHub API error: {response.StatusCode}";

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException("GitHub access token is invalid or expired.");
                case System.Net.HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException("Insufficient permissions to access repository information.");
                case System.Net.HttpStatusCode.TooManyRequests:
                    throw new InvalidOperationException("GitHub API rate limit exceeded. Please try again later.");
                default:
                    throw new HttpRequestException($"{errorMessage}. Response: {errorContent}");
            }
        }
        catch (TaskCanceledException)
        {
            throw new TaskCanceledException("Repository check request timed out.");
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw authorization exceptions
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw validation exceptions
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error checking repository: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the authenticated user's repositories
    /// </summary>
    public async Task<GitHubRepository[]> GetUserRepositoriesAsync(string accessToken, bool includePrivate = true)
    {
        try
        {
            var url = $"{GitHubConstants.ApiBaseUrl}/user/repos?sort=updated&direction=desc&per_page=100";
            if (!includePrivate)
                url += "&visibility=public";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GitHubRepository[]>(json, JsonOptions) ?? Array.Empty<GitHubRepository>();
            }

            // Handle specific HTTP error codes
            string errorContent = await response.Content.ReadAsStringAsync();
            string errorMessage = $"GitHub API error: {response.StatusCode}";

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException("GitHub access token is invalid or expired.");
                case System.Net.HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException("Insufficient permissions to access repository information.");
                case System.Net.HttpStatusCode.TooManyRequests:
                    throw new InvalidOperationException("GitHub API rate limit exceeded. Please try again later.");
                default:
                    throw new HttpRequestException($"{errorMessage}. Response: {errorContent}");
            }
        }
        catch (TaskCanceledException)
        {
            throw new TaskCanceledException("Repository list request timed out.");
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP exceptions
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw authorization exceptions
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw validation exceptions
        }
        catch (JsonException jsonEx)
        {
            throw new InvalidOperationException($"Error parsing GitHub API response: {jsonEx.Message}", jsonEx);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error fetching repositories: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets detailed information about a repository
    /// </summary>
    public async Task<GitHubRepository?> GetRepositoryInfoAsync(string accessToken, string owner, string repositoryName)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubConstants.ApiBaseUrl}/repos/{owner}/{repositoryName}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitHubRepository>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates a repository name according to GitHub's rules
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateRepositoryName(string repositoryName)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
            return (false, "Repository name cannot be empty");

        if (repositoryName.Length > 100)
            return (false, "Repository name cannot exceed 100 characters");

        if (repositoryName.StartsWith(".") || repositoryName.EndsWith("."))
            return (false, "Repository name cannot start or end with a period");

        if (repositoryName.StartsWith("-") || repositoryName.EndsWith("-"))
            return (false, "Repository name cannot start or end with a hyphen");

        if (repositoryName.Contains(".."))
            return (false, "Repository name cannot contain consecutive periods");

        // GitHub repository names can contain alphanumeric characters, hyphens, periods, and underscores
        if (!System.Text.RegularExpressions.Regex.IsMatch(repositoryName, @"^[a-zA-Z0-9._-]+$"))
            return (false, "Repository name can only contain letters, numbers, hyphens, periods, and underscores");

        // Reserved names
        var reservedNames = new[] { ".", "..", "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reservedNames.Contains(repositoryName.ToUpperInvariant()))
            return (false, $"'{repositoryName}' is a reserved name and cannot be used");

        return (true, null);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
