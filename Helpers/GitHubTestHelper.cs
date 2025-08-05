using GitMC.Constants;
using GitMC.Models.GitHub;
using GitMC.Services;

namespace GitMC.Helpers;

/// <summary>
///     Helper class for testing GitHub Apps integration
/// </summary>
public static class GitHubTestHelper
{
    /// <summary>
    ///     Tests the complete GitHub Device Flow authentication process
    /// </summary>
    /// <returns>Authentication result with details</returns>
    public static async Task<GitHubAuthResult> TestDeviceFlowAsync()
    {
        var gitHubService = new GitHubAppsService();

        try
        {
            Console.WriteLine("Starting GitHub Device Flow test...");

            // Start device flow
            var deviceCode = await gitHubService.StartDeviceFlowAsync();
            if (deviceCode == null)
            {
                return new GitHubAuthResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Failed to start device flow"
                };
            }

            Console.WriteLine($"Device Flow started successfully:");
            Console.WriteLine($"  User Code: {deviceCode.UserCode}");
            Console.WriteLine($"  Verification URL: {deviceCode.VerificationUri}");
            Console.WriteLine($"  Expires in: {deviceCode.ExpiresIn} seconds");
            Console.WriteLine();
            Console.WriteLine("Please visit the URL above and enter the user code.");
            Console.WriteLine("Press any key after authorizing...");
            Console.ReadKey();

            // Poll for completion
            var result = await gitHubService.PollDeviceFlowAsync(deviceCode.DeviceCode, CancellationToken.None);

            if (result.IsSuccess)
            {
                Console.WriteLine($"✅ Authentication successful!");
                Console.WriteLine($"  Username: {result.User?.Login}");
                Console.WriteLine($"  User ID: {result.User?.Id}");
                Console.WriteLine($"  Email: {result.User?.Email}");
                Console.WriteLine($"  Token: {result.AccessToken?[..10]}...");
            }
            else
            {
                Console.WriteLine($"❌ Authentication failed: {result.ErrorMessage}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed with exception: {ex.Message}");
            return new GitHubAuthResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            gitHubService.Dispose();
        }
    }

    /// <summary>
    ///     Tests token validation with a provided access token
    /// </summary>
    /// <param name="accessToken">GitHub access token to test</param>
    /// <returns>True if token is valid</returns>
    public static async Task<bool> TestTokenValidationAsync(string accessToken)
    {
        var gitHubService = new GitHubAppsService();

        try
        {
            Console.WriteLine("Testing token validation...");

            var isValid = await gitHubService.ValidateTokenAsync(accessToken);

            if (isValid)
            {
                var user = await gitHubService.GetUserAsync(accessToken);
                Console.WriteLine($"✅ Token is valid for user: {user?.Login}");
            }
            else
            {
                Console.WriteLine("❌ Token is invalid or expired");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Token validation failed: {ex.Message}");
            return false;
        }
        finally
        {
            gitHubService.Dispose();
        }
    }

    /// <summary>
    ///     Tests repository creation
    /// </summary>
    /// <param name="accessToken">Valid GitHub access token</param>
    /// <param name="repositoryName">Name of repository to create</param>
    /// <returns>True if repository was created successfully</returns>
    public static async Task<bool> TestRepositoryCreationAsync(string accessToken, string repositoryName = "gitmc-test-repo")
    {
        var gitHubService = new GitHubAppsService();

        try
        {
            Console.WriteLine($"Testing repository creation: {repositoryName}...");

            var success = await gitHubService.CreateRepositoryAsync(
                accessToken,
                repositoryName,
                isPrivate: true,
                description: "Test repository created by GitMC");

            if (success)
            {
                Console.WriteLine($"✅ Repository '{repositoryName}' created successfully");
            }
            else
            {
                Console.WriteLine($"❌ Failed to create repository '{repositoryName}'");
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Repository creation failed: {ex.Message}");
            return false;
        }
        finally
        {
            gitHubService.Dispose();
        }
    }
}
