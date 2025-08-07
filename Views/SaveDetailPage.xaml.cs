using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using GitMC.Constants;
using GitMC.Extensions;
using GitMC.Models;
using GitMC.Models.GitHub;
using GitMC.Services;
using GitMC.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace GitMC.Views;

// Helper class for branch combo box items
public class BranchComboBoxItem
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsSeparator { get; set; }
    public bool IsCreateAction { get; set; }
    public string? BranchName { get; set; }
}

public sealed partial class SaveDetailPage : Page, INotifyPropertyChanged
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitHubAppsService _gitHubAppsService;
    private readonly IGitService _gitService;
    private readonly ManagedSaveService _managedSaveService;
    private readonly IMinecraftAnalyzerService _minecraftAnalyzerService;
    private readonly NbtService _nbtService;
    private readonly ISaveInitializationService _saveInitializationService;
    private string? _saveId;
    private Timer? _changeDetectionTimer;

    public SaveDetailPage()
    {
        InitializeComponent();

        // Use ServiceFactory to get shared service instances
        var services = ServiceFactory.Services;
        _nbtService = services.Nbt as NbtService ?? new NbtService();
        _configurationService = services.Configuration;
        _gitService = services.Git;
        _dataStorageService = services.DataStorage;
        _saveInitializationService = services.SaveInitialization;
        _minecraftAnalyzerService = ServiceFactory.MinecraftAnalyzer;
        _managedSaveService = new ManagedSaveService(_dataStorageService);
        _gitHubAppsService = ServiceFactory.GitHubApps;

        ViewModel = new SaveDetailViewModel();
        DataContext = this;
    }

    public SaveDetailViewModel ViewModel { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string saveId)
        {
            _saveId = saveId;

            // Initialize the page asynchronously on the UI thread
            _ = InitializePageAsync();
        }
    }

    private async Task InitializePageAsync()
    {
        try
        {
            // Small delay to ensure XAML is fully loaded
            await Task.Delay(50);

            // Initialize the page with the selected save
            await LoadSaveDetailAsync();

            // Load available branches
            LoadBranches();

            // Navigate to the default tab (Overview)
            NavigateToTab("Overview");

            // Start periodic change detection updates
            StartChangeDetectionUpdates();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in InitializePageAsync: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private async Task LoadOverviewDataAsync()
    {
        try
        {
            if (ViewModel.SaveInfo == null) return;

            // Load recent commits
            await LoadRecentCommitsAsync();

            // Load remote information
            await LoadRemoteInfoAsync();

            // Update overview UI elements
            UpdateOverviewUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading overview data: {ex.Message}");
        }
    }

    private async Task LoadRecentCommitsAsync()
    {
        try
        {
            if (ViewModel.SaveInfo?.IsGitInitialized == true && !string.IsNullOrEmpty(ViewModel.SaveInfo.OriginalPath))
            {
                var commits = await _gitService.GetCommitHistoryAsync(5, ViewModel.SaveInfo.OriginalPath);
                var commitInfos = commits.Select(commit => new CommitInfo
                {
                    Sha = commit.Sha,
                    Message = commit.Message,
                    Author = commit.AuthorName,
                    Timestamp = commit.AuthorDate
                }).ToList();

                ViewModel.RecentCommits.Clear();
                foreach (var commit in commitInfos) ViewModel.RecentCommits.Add(commit);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading recent commits: {ex.Message}");
            // Add placeholder commits for now
            ViewModel.RecentCommits.Clear();
            ViewModel.RecentCommits.Add(new CommitInfo
            {
                Message = "Initial commit",
                Author = "User",
                Timestamp = DateTime.Now.AddHours(-2)
            });
        }
    }

    private async Task LoadRemoteInfoAsync()
    {
        // Changed: Switch UI based on GitHub token and save-specific repo state
        var notLoggedInPanel = FindName("GitHubNotLoggedInPanel") as StackPanel;
        var noRepoPanel = FindName("GitHubNoRepoPanel") as StackPanel;
        var linkedPanel = FindName("GitHubRepoLinkedPanel") as StackPanel;
        if (notLoggedInPanel == null || noRepoPanel == null || linkedPanel == null) return;

        // Hide all by default
        notLoggedInPanel.Visibility = Visibility.Collapsed;
        noRepoPanel.Visibility = Visibility.Collapsed;
        linkedPanel.Visibility = Visibility.Collapsed;

        // Check if GitHub platform is selected
        if (_configurationService.SelectedPlatform != "GitHub")
        {
            notLoggedInPanel.Visibility = Visibility.Visible;
            return;
        }

        // Check GitHub token
        var token = _configurationService.GitHubAccessToken;
        var tokenTime = _configurationService.GitHubAccessTokenTimestamp;

        if (string.IsNullOrEmpty(token))
        {
            notLoggedInPanel.Visibility = Visibility.Visible;
            return;
        }

        var tokenState = await _gitHubAppsService.ValidateTokenStateAsync(token, tokenTime);
        if (!tokenState.IsValid)
        {
            notLoggedInPanel.Visibility = Visibility.Visible;
            return;
        }

        // Check if this save has a linked repo (save-specific configuration)
        if (ViewModel.SaveInfo == null || !ViewModel.SaveInfo.IsGitHubLinked ||
            string.IsNullOrWhiteSpace(ViewModel.SaveInfo.GitHubRepositoryName))
        {
            noRepoPanel.Visibility = Visibility.Visible;
            return;
        }

        // Repo linked, show info
        linkedPanel.Visibility = Visibility.Visible;
        // Username, avatar
        var user = await _gitHubAppsService.GetUserAsync(token);
        if (FindName("GitHubAvatar") is Image avatar && user?.AvatarUrl is string avatarUrl)
            avatar.Source = new BitmapImage(new Uri(avatarUrl));
        if (FindName("GitHubUserNameText") is TextBlock userNameText)
            userNameText.Text = user?.Login ?? "";
        // Repo link
        if (FindName("GitHubRepoLink") is HyperlinkButton repoLink)
        {
            repoLink.Content = ViewModel.SaveInfo.GitHubRepositoryName;
            repoLink.NavigateUri =
                new Uri($"https://github.com/{user?.Login}/{ViewModel.SaveInfo.GitHubRepositoryName}");
        }

        // Repo description, visibility, branch, remote URL
        if (FindName("GitHubRepoDescText") is TextBlock descText)
            descText.Text = ViewModel.SaveInfo.GitHubRepositoryDescription;
        if (FindName("GitHubRepoVisibilityText") is TextBlock visText)
            visText.Text = ViewModel.SaveInfo.GitHubIsPrivateRepository ? "Private" : "Public";
        if (FindName("GitHubRepoBranchText") is TextBlock branchText)
            branchText.Text = ViewModel.SaveInfo.GitHubDefaultBranch;
        if (FindName("GitHubRemoteUrlBox") is TextBox remoteBox)
            remoteBox.Text = ViewModel.SaveInfo.GitHubRemoteUrl;
    }

    // GitHub sign-in button event
    private async void SignInGitHub_Click(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<string>(msg => Debug.WriteLine(msg));
        var result = await _gitHubAppsService.AuthenticateWithDeviceFlowAsync(progress);
        if (result.IsSuccess && result.AccessToken != null)
        {
            _configurationService.SelectedPlatform = "GitHub";
            _configurationService.GitHubAccessToken = result.AccessToken;
            _configurationService.GitHubAccessTokenTimestamp = DateTime.UtcNow;
            _configurationService.GitHubUsername = result.User?.Login ?? "";
            await _configurationService.SaveAsync();
            await LoadRemoteInfoAsync();
        }
        else
        {
            ContentDialog dialog = new()
            {
                Title = "GitHub Sign-in Failed",
                Content = result.ErrorMessage ?? "Unknown error",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    // Create and link repository (pseudo-action for testing)
    private async void CreateAndLinkRepo_Click(object sender, RoutedEventArgs e)
    {
        // Prevent multiple clicks
        if (sender is Button repoCreateButton) repoCreateButton.IsEnabled = false;

        ContentDialog? progressDialog = null;

        try
        {
            // Validate UI elements
            if (FindName("GitHubRepoNameBox") is not TextBox nameBox ||
                FindName("GitHubRepoDescBox") is not TextBox descBox ||
                FindName("GitHubRepoPrivateBox") is not CheckBox privateBox)
            {
                await ShowErrorDialogSafe("UI Error", "Unable to access form controls. Please try again.");
                return;
            }

            var repoName = nameBox.Text.Trim();
            var desc = descBox.Text.Trim();
            var isPrivate = privateBox.IsChecked ?? true;

            // Validate repository name
            try
            {
                var (isValidName, nameError) = GitHubAppsService.ValidateRepositoryName(repoName);
                if (!isValidName)
                {
                    await ShowErrorDialogSafe("Invalid Repository Name", nameError ?? "Repository name is invalid.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Repository name validation failed: {ex.Message}");
                await ShowErrorDialogSafe("Validation Error",
                    "Unable to validate repository name. Please check the name and try again.");
                return;
            }

            // Get and validate access token
            var token = _configurationService.GitHubAccessToken;
            var tokenTime = _configurationService.GitHubAccessTokenTimestamp;

            if (string.IsNullOrEmpty(token))
            {
                await ShowErrorDialogSafe("Authentication Required", "Please sign in to GitHub first.");
                return;
            }

            // Validate token state
            try
            {
                var (isTokenValid, isExpired, tokenError) =
                    await _gitHubAppsService.ValidateTokenStateAsync(token, tokenTime);
                if (!isTokenValid)
                {
                    var errorMsg = isExpired
                        ? "Your GitHub access token has expired. Please sign in again."
                        : tokenError ?? "GitHub access token is invalid.";
                    await ShowErrorDialogSafe("Authentication Error", errorMsg);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token validation failed: {ex.Message}");
                await ShowErrorDialogSafe("Authentication Error",
                    "Unable to validate GitHub authentication. Please check your connection and try signing in again.");
                return;
            }

            // Create and show progress dialog
            progressDialog = new ContentDialog
            {
                Title = "Processing Repository",
                Content = new StackPanel
                {
                    Children =
                    {
                        new ProgressRing { IsActive = true, Margin = new Thickness(0, 0, 0, 16) },
                        new TextBlock
                        {
                            Text = "Setting up repository configuration...",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                },
                IsPrimaryButtonEnabled = false,
                IsSecondaryButtonEnabled = false,
                XamlRoot = XamlRoot
            };

            // Show progress dialog without await to prevent blocking
            _ = progressDialog.ShowAsync();

            // Simulate repository creation delay
            await Task.Delay(1500);

            // PSEUDO-ACTION: Save configuration without actually creating repository
            try
            {
                if (ViewModel.SaveInfo != null)
                {
                    ViewModel.SaveInfo.GitHubRepositoryName = repoName;
                    ViewModel.SaveInfo.GitHubIsPrivateRepository = isPrivate;
                    ViewModel.SaveInfo.GitHubRepositoryDescription = desc;
                    ViewModel.SaveInfo.GitHubDefaultBranch = "main";
                    var user = _configurationService.GitHubUsername;
                    var remoteUrl = $"https://github.com/{user}/{repoName}.git";
                    ViewModel.SaveInfo.GitHubRemoteUrl = remoteUrl;
                    ViewModel.SaveInfo.IsGitHubLinked = true;

                    // Update the save info in storage
                    await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);

                    // Simulate adding remote to local repo if git is initialized
                    if (ViewModel.SaveInfo.IsGitInitialized && !string.IsNullOrEmpty(ViewModel.SaveInfo.OriginalPath))
                        try
                        {
                            // In actual implementation, this would add the remote
                            // For now, we just simulate it being successful
                            Debug.WriteLine($"Would add remote 'origin' with URL: {remoteUrl}");
                        }
                        catch (Exception gitEx)
                        {
                            Debug.WriteLine($"Error simulating git remote addition: {gitEx.Message}");
                        }
                }

                // Clear input fields
                nameBox.Text = string.Empty;
                descBox.Text = string.Empty;

                await LoadRemoteInfoAsync();

                // Show temporary success InfoBar
                await ShowTemporarySuccessInfoBar();

                // Show success message with pseudo-action indication
                await ShowSuccessDialogSafe("Repository Configuration Saved",
                    $"Repository configuration for '{repoName}' has been saved successfully!\n\n" +
                    "Note: This is a pseudo-action. The actual GitHub repository creation will be implemented in the future.");
            }
            catch (Exception configEx)
            {
                Debug.WriteLine($"Error saving configuration: {configEx.Message}");
                await ShowErrorDialogSafe("Configuration Error",
                    $"Failed to save repository configuration: {configEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unexpected error in CreateAndLinkRepo_Click: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            await ShowErrorDialogSafe("Unexpected Error",
                $"An unexpected error occurred: {ex.Message}. Please try again or contact support if the problem persists.");
        }
        finally
        {
            // Ensure progress dialog is closed and button is re-enabled
            try
            {
                progressDialog?.Hide();
            }
            catch (Exception hideEx)
            {
                Debug.WriteLine($"Error hiding progress dialog: {hideEx.Message}");
            }

            if (sender is Button repoButton) repoButton.IsEnabled = true;
        }
    }

    // Generate pseudo repository data for testing
    private GitHubRepository[] GeneratePseudoRepositories()
    {
        var pseudoRepos = new List<GitHubRepository>
        {
            new()
            {
                Id = 1,
                Name = "my-minecraft-world",
                FullName = "TestUser/my-minecraft-world",
                Description = "My awesome Minecraft survival world",
                IsPrivate = false,
                HtmlUrl = "https://github.com/TestUser/my-minecraft-world",
                CloneUrl = "https://github.com/TestUser/my-minecraft-world.git",
                SshUrl = "git@github.com:TestUser/my-minecraft-world.git",
                DefaultBranch = "main",
                IsEmpty = false,
                Size = 512000,
                UpdatedAt = DateTime.Now.AddHours(-2),
                CreatedAt = DateTime.Now.AddDays(-30),
                Owner = new GitHubUser
                {
                    Login = "TestUser",
                    Id = 12345,
                    Name = "Test User",
                    AvatarUrl = "https://github.com/identicons/testuser.png"
                }
            },
            new()
            {
                Id = 2,
                Name = "creative-builds",
                FullName = "TestUser/creative-builds",
                Description = "Collection of my creative mode buildings and redstone contraptions",
                IsPrivate = true,
                HtmlUrl = "https://github.com/TestUser/creative-builds",
                CloneUrl = "https://github.com/TestUser/creative-builds.git",
                SshUrl = "git@github.com:TestUser/creative-builds.git",
                DefaultBranch = "main",
                IsEmpty = false,
                Size = 256000,
                UpdatedAt = DateTime.Now.AddDays(-1),
                CreatedAt = DateTime.Now.AddDays(-60),
                Owner = new GitHubUser
                {
                    Login = "TestUser",
                    Id = 12345,
                    Name = "Test User",
                    AvatarUrl = "https://github.com/identicons/testuser.png"
                }
            },
            new()
            {
                Id = 3,
                Name = "modded-adventures",
                FullName = "TestUser/modded-adventures",
                Description = "Modded Minecraft gameplay saves and configurations",
                IsPrivate = false,
                HtmlUrl = "https://github.com/TestUser/modded-adventures",
                CloneUrl = "https://github.com/TestUser/modded-adventures.git",
                SshUrl = "git@github.com:TestUser/modded-adventures.git",
                DefaultBranch = "main",
                IsEmpty = true,
                Size = 0,
                UpdatedAt = DateTime.Now.AddDays(-7),
                CreatedAt = DateTime.Now.AddDays(-7),
                Owner = new GitHubUser
                {
                    Login = "TestUser",
                    Id = 12345,
                    Name = "Test User",
                    AvatarUrl = "https://github.com/identicons/testuser.png"
                }
            },
            new()
            {
                Id = 4,
                Name = "server-maps",
                FullName = "TestUser/server-maps",
                Description = "Backup of multiplayer server worlds",
                IsPrivate = true,
                HtmlUrl = "https://github.com/TestUser/server-maps",
                CloneUrl = "https://github.com/TestUser/server-maps.git",
                SshUrl = "git@github.com:TestUser/server-maps.git",
                DefaultBranch = "master",
                IsEmpty = false,
                Size = 1024000,
                UpdatedAt = DateTime.Now.AddHours(-6),
                CreatedAt = DateTime.Now.AddDays(-90),
                Owner = new GitHubUser
                {
                    Login = "TestUser",
                    Id = 12345,
                    Name = "Test User",
                    AvatarUrl = "https://github.com/identicons/testuser.png"
                }
            },
            new()
            {
                Id = 5,
                Name = "empty-repo",
                FullName = "TestUser/empty-repo",
                Description = "A new empty repository for testing",
                IsPrivate = false,
                HtmlUrl = "https://github.com/TestUser/empty-repo",
                CloneUrl = "https://github.com/TestUser/empty-repo.git",
                SshUrl = "git@github.com:TestUser/empty-repo.git",
                DefaultBranch = "main",
                IsEmpty = true,
                Size = 0,
                UpdatedAt = DateTime.Now.AddMinutes(-30),
                CreatedAt = DateTime.Now.AddMinutes(-30),
                Owner = new GitHubUser
                {
                    Login = "TestUser",
                    Id = 12345,
                    Name = "Test User",
                    AvatarUrl = "https://github.com/identicons/testuser.png"
                }
            }
        };

        return pseudoRepos.ToArray();
    }

    // Link existing repository (with pseudo repository selector)
    private async void LinkExistingRepo_Click(object sender, RoutedEventArgs e)
    {
        // Prevent multiple clicks
        if (sender is Button linkButton) linkButton.IsEnabled = false;

        ContentDialog? loadingDialog = null;

        try
        {
            // Get and validate access token
            var token = _configurationService.GitHubAccessToken;
            var tokenTime = _configurationService.GitHubAccessTokenTimestamp;

            if (string.IsNullOrEmpty(token))
            {
                await ShowErrorDialogSafe("Authentication Required", "Please sign in to GitHub first.");
                return;
            }

            // Validate token state
            try
            {
                var (isTokenValid, isExpired, tokenError) =
                    await _gitHubAppsService.ValidateTokenStateAsync(token, tokenTime);
                if (!isTokenValid)
                {
                    var errorMsg = isExpired
                        ? "Your GitHub access token has expired. Please sign in again."
                        : tokenError ?? "GitHub access token is invalid.";
                    await ShowErrorDialogSafe("Authentication Error", errorMsg);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token validation failed: {ex.Message}");
                await ShowErrorDialogSafe("Authentication Error",
                    "Unable to validate GitHub authentication. Please check your connection and try signing in again.");
                return;
            }

            // Create progress dialog
            loadingDialog = new ContentDialog
            {
                Title = "Loading Repositories",
                Content = new StackPanel
                {
                    Children =
                    {
                        new ProgressRing { IsActive = true, Margin = new Thickness(0, 0, 0, 16) },
                        new TextBlock
                        {
                            Text = "Fetching your repositories...",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                },
                IsPrimaryButtonEnabled = false,
                IsSecondaryButtonEnabled = false,
                XamlRoot = XamlRoot
            };

            // Show loading dialog without await to prevent blocking
            _ = loadingDialog.ShowAsync();

            // Simulate loading delay
            await Task.Delay(1000);

            // PSEUDO-ACTION: Use generated pseudo repository data instead of API call
            GitHubRepository[] repositories;
            try
            {
                repositories = GeneratePseudoRepositories();
                Debug.WriteLine($"Generated {repositories.Length} pseudo repositories for testing");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating pseudo repositories: {ex.Message}");
                await ShowErrorDialogSafe("Data Error",
                    $"Unable to generate test repository data: {ex.Message}. Please try again later.");
                return;
            }

            if (repositories.Length == 0)
            {
                await ShowInfoDialogSafe("No Repositories Found",
                    "No test repositories available. Create a new repository first.\n\n" +
                    "Note: This is using pseudo data for testing purposes.");
                return;
            }

            // Filter out repositories that are already linked or too large
            var availableRepos = repositories
                .Where(repo => repo.IsEmpty || repo.Size < 1024 * 1024) // Less than 1GB
                .ToList();

            if (availableRepos.Count == 0)
            {
                await ShowInfoDialogSafe("No Available Repositories",
                    "All test repositories are either too large or already in use. Consider creating a new repository.\n\n" +
                    "Note: This is using pseudo data for testing purposes.");
                return;
            }

            // Hide loading dialog before showing selection dialog
            try
            {
                loadingDialog?.Hide();
                loadingDialog = null;
            }
            catch (Exception hideEx)
            {
                Debug.WriteLine($"Error hiding loading dialog: {hideEx.Message}");
            }

            // Create repository selection dialog
            ContentDialog repoSelectionDialog = new()
            {
                Title = "Select Repository (Test Data)",
                PrimaryButtonText = "Link",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var panel = new StackPanel { Spacing = 12 };

            // Add info about pseudo data
            var infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                IsOpen = true,
                Title = "Test Mode",
                Message =
                    "This is using pseudo data for testing purposes. Actual GitHub API integration will be implemented in the future."
            };
            panel.Children.Add(infoBar);

            panel.Children.Add(new TextBlock
            {
                Text = "Select an existing repository to link to this save:",
                Margin = new Thickness(0, 8, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            var repoComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "Choose a repository..."
            };

            foreach (var repo in availableRepos)
            {
                var statusEmoji = repo.IsPrivate ? "🔒" : "🌐";
                var sizeInfo = repo.IsEmpty ? "Empty" : $"{repo.Size / 1024}KB";
                var item = new ComboBoxItem
                {
                    Content = $"{repo.Name} {statusEmoji} - {repo.Description ?? "No description"} ({sizeInfo})",
                    Tag = repo
                };
                repoComboBox.Items.Add(item);
            }

            panel.Children.Add(repoComboBox);

            var branchBox = new TextBox
            {
                PlaceholderText = "Default branch (e.g., main)",
                Text = "main",
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(new TextBlock { Text = "Default Branch:", Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(branchBox);

            repoSelectionDialog.Content = panel;

            var result = await repoSelectionDialog.ShowAsync();
            if (result == ContentDialogResult.Primary && repoComboBox.SelectedItem is ComboBoxItem selectedItem &&
                selectedItem.Tag is GitHubRepository selectedRepo)
            {
                var branch = branchBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(branch))
                    branch = selectedRepo.DefaultBranch;

                // Save to save-specific configuration
                try
                {
                    if (ViewModel.SaveInfo != null)
                    {
                        ViewModel.SaveInfo.GitHubRepositoryName = selectedRepo.Name;
                        ViewModel.SaveInfo.GitHubRepositoryDescription = selectedRepo.Description ?? "";
                        ViewModel.SaveInfo.GitHubIsPrivateRepository = selectedRepo.IsPrivate;
                        ViewModel.SaveInfo.GitHubDefaultBranch = branch;
                        ViewModel.SaveInfo.GitHubRemoteUrl = selectedRepo.CloneUrl;
                        ViewModel.SaveInfo.IsGitHubLinked = true;

                        // Update the save info in storage
                        await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);

                        // Simulate adding remote to local repo if git is initialized
                        if (ViewModel.SaveInfo.IsGitInitialized &&
                            !string.IsNullOrEmpty(ViewModel.SaveInfo.OriginalPath))
                            try
                            {
                                // In actual implementation, this would add the remote
                                // For now, we just simulate it being successful
                                Debug.WriteLine($"Would add remote 'origin' with URL: {selectedRepo.CloneUrl}");
                            }
                            catch (Exception gitEx)
                            {
                                Debug.WriteLine($"Error simulating git remote addition: {gitEx.Message}");
                            }
                    }

                    await LoadRemoteInfoAsync();

                    // Show temporary success InfoBar
                    await ShowTemporarySuccessInfoBar();

                    // Show success message with pseudo-action indication
                    await ShowSuccessDialogSafe("Repository Linked",
                        $"Repository '{selectedRepo.Name}' has been linked to this save successfully!\n\n" +
                        "Note: This used pseudo data. Actual GitHub integration will be implemented in the future.");
                }
                catch (Exception configEx)
                {
                    Debug.WriteLine($"Error saving configuration: {configEx.Message}");
                    await ShowErrorDialogSafe("Configuration Error",
                        $"Failed to save repository configuration: {configEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unexpected error in LinkExistingRepo_Click: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            await ShowErrorDialogSafe("Unexpected Error",
                $"An unexpected error occurred: {ex.Message}. Please try again or contact support if the problem persists.");
        }
        finally
        {
            // Ensure loading dialog is closed and button is re-enabled
            try
            {
                loadingDialog?.Hide();
            }
            catch (Exception hideEx)
            {
                Debug.WriteLine($"Error hiding loading dialog: {hideEx.Message}");
            }

            if (sender is Button linkRepoButton) linkRepoButton.IsEnabled = true;
        }
    }

    private async Task ShowInfoDialog(string message)
    {
        await ShowInfoDialogSafe("Info", message);
    }

    private async Task ShowInfoDialog(string title, string message)
    {
        await ShowInfoDialogSafe(title, message);
    }

    private async Task ShowInfoDialogSafe(string title, string message)
    {
        try
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing info dialog: {ex.Message}");
            // Fallback to debug output if dialog fails
            Debug.WriteLine($"Info Dialog - {title}: {message}");
        }
    }

    private async Task ShowErrorDialog(string title, string message)
    {
        await ShowErrorDialogSafe(title, message);
    }

    private async Task ShowErrorDialogSafe(string title, string message)
    {
        try
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = new StackPanel
                {
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE783", // Error icon
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
                            FontSize = 20,
                            Margin = new Thickness(0, 0, 0, 8),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing error dialog: {ex.Message}");
            // Fallback to debug output if dialog fails
            Debug.WriteLine($"Error Dialog - {title}: {message}");
        }
    }

    private async Task ShowSuccessDialog(string title, string message)
    {
        await ShowSuccessDialogSafe(title, message);
    }

    private async Task ShowSuccessDialogSafe(string title, string message)
    {
        try
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = new StackPanel
                {
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE73E", // CheckMark icon
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),
                            FontSize = 20,
                            Margin = new Thickness(0, 0, 0, 8),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing success dialog: {ex.Message}");
            // Fallback to debug output if dialog fails
            Debug.WriteLine($"Success Dialog - {title}: {message}");
        }
    }

    private async Task ShowWarningDialog(string title, string message)
    {
        await ShowWarningDialogSafe(title, message);
    }

    private async Task ShowWarningDialogSafe(string title, string message)
    {
        try
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = new StackPanel
                {
                    Children =
                    {
                        new FontIcon
                        {
                            Glyph = "\uE7BA", // Warning icon
                            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 185, 0)),
                            FontSize = 20,
                            Margin = new Thickness(0, 0, 0, 8),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing warning dialog: {ex.Message}");
            // Fallback to debug output if dialog fails
            Debug.WriteLine($"Warning Dialog - {title}: {message}");
        }
    }

    private async Task ShowTemporarySuccessInfoBar()
    {
        try
        {
            if (FindName("GitHubLinkSuccessInfoBar") is InfoBar successInfoBar)
            {
                successInfoBar.IsOpen = true;

                // Auto-hide after 5 seconds
                await Task.Delay(5000);
                successInfoBar.IsOpen = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error showing temporary success InfoBar: {ex.Message}");
        }
    }

    private void UpdateOverviewUI()
    {
        if (ViewModel.SaveInfo == null) return;

        // Update Remote Status
        UpdateRemoteStatus();
        UpdateStatusDisplay();
    }

    /// <summary>
    ///     Updates the premium-styled status display with current save information
    /// </summary>
    private void UpdateStatusDisplay()
    {
        if (ViewModel.SaveInfo == null) return;

        try
        {
            // Update Current Branch Display Text
            if (FindName("CurrentBranchDisplayText") is TextBlock branchDisplayText)
                branchDisplayText.Text = ViewModel.SaveInfo.Branch ?? "main";

            // Update Sync Status Badge
            UpdateSyncStatusBadge();

            // Update Status InfoBar
            UpdateStatusInfoBar();

            // Update Sync Progress
            UpdateSyncProgressDisplay();

            // Update File Changes Summary
            UpdateFileChangesSummary();

            // Update Branch Tags Container
            UpdateBranchTagsDisplay();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating premium status display: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates the sync status badge with appropriate colors and text
    /// </summary>
    private void UpdateSyncStatusBadge()
    {
        if (FindName("SyncStatusBadge") is Border syncStatusBadge &&
            FindName("SyncStatusText") is TextBlock syncStatusText &&
            ViewModel.SaveInfo != null)
        {
            var (statusText, backgroundColor, borderColor, textColor) = GetSyncStatusInfo(ViewModel.SaveInfo);

            syncStatusText.Text = statusText;
            syncStatusBadge.Background = new SolidColorBrush(backgroundColor);
            syncStatusBadge.BorderBrush = new SolidColorBrush(borderColor);
            syncStatusText.Foreground = new SolidColorBrush(textColor);
        }
    }

    /// <summary>
    ///     Updates the main status InfoBar with current save status
    /// </summary>
    private void UpdateStatusInfoBar()
    {
        if (FindName("StatusInfoBar") is InfoBar statusInfoBar && ViewModel.SaveInfo != null)
        {
            statusInfoBar.Title = GetStatusTitle(ViewModel.SaveInfo);
            statusInfoBar.Message = GetStatusDescription(ViewModel.SaveInfo);
            statusInfoBar.Severity = GetStatusSeverity(ViewModel.SaveInfo);
            statusInfoBar.IsOpen = true;
        }
    }

    /// <summary>
    ///     Updates the sync progress bar and percentage text
    /// </summary>
    private void UpdateSyncProgressDisplay()
    {
        if (FindName("SyncProgressBar") is ProgressBar syncProgressBar &&
            FindName("SyncProgressText") is TextBlock syncProgressText &&
            ViewModel.SaveInfo != null)
        {
            var syncProgress = CalculateSyncProgress(ViewModel.SaveInfo);
            syncProgressBar.Value = syncProgress;
            syncProgressText.Text = $"{syncProgress:F0}%";
        }
    }

    /// <summary>
    ///     Updates the file changes summary section with current statistics
    /// </summary>
    private async void UpdateFileChangesSummary()
    {
        if (ViewModel.SaveInfo == null) return;

        var fileBreakdown = await CalculateFileBreakdownAsync(ViewModel.SaveInfo);

        // Update individual file type counts
        if (FindName("RegionChunksCountText") is TextBlock regionChunksText)
            regionChunksText.Text = fileBreakdown.RegionChunks.ToString();

        if (FindName("WorldDataCountText") is TextBlock worldDataText)
            worldDataText.Text = fileBreakdown.WorldData.ToString();

        if (FindName("PlayerDataCountText") is TextBlock playerDataText)
            playerDataText.Text = fileBreakdown.PlayerData.ToString();

        if (FindName("EntityDataCountText") is TextBlock entityDataText)
            entityDataText.Text = fileBreakdown.EntityData.ToString();

        if (FindName("StructureDataCountText") is TextBlock structureDataText)
            structureDataText.Text = fileBreakdown.StructureData.ToString();

        // Update changes summary
        if (FindName("AddedFilesText") is TextBlock addedText)
            addedText.Text = fileBreakdown.AddedFiles > 0 ? $"+{fileBreakdown.AddedFiles}" : "0";

        if (FindName("DeletedFilesText") is TextBlock deletedText)
            deletedText.Text = fileBreakdown.DeletedFiles > 0 ? $"-{fileBreakdown.DeletedFiles}" : "0";

        if (FindName("TotalFilesText") is TextBlock totalText)
            totalText.Text = fileBreakdown.TotalFiles.ToString();
    }

    /// <summary>
    ///     Updates the branch tags display with available branches
    /// </summary>
    private void UpdateBranchTagsDisplay()
    {
        if (FindName("BranchTagsContainer") is StackPanel branchContainer && ViewModel.SaveInfo != null)
        {
            branchContainer.Children.Clear();
            var branches = GetAvailableBranches(ViewModel.SaveInfo);

            foreach (var branch in branches.Take(3)) // Show max 3 branches
            {
                var branchBorder = new Border
                {
                    Background = branch == ViewModel.SaveInfo.Branch
                        ? new SolidColorBrush(ColorConstants.BadgeColors
                            .InfoText) // Use InfoText blue for current branch
                        : new SolidColorBrush(ColorConstants.CardBackground), // Use CardBackground for other branches
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 3, 6, 3)
                };

                var branchText = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = branch == ViewModel.SaveInfo.Branch ? FontWeights.Medium : FontWeights.Normal,
                    Foreground = branch == ViewModel.SaveInfo.Branch
                        ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) // White text for current branch
                        : new SolidColorBrush(ColorConstants.SecondaryText), // Use SecondaryText for other branches
                    Text = branch
                };

                branchBorder.Child = branchText;
                branchContainer.Children.Add(branchBorder);
            }
        }
    }

    /// <summary>
    ///     Refreshes all status displays with current save information
    /// </summary>
    public void RefreshStatusDisplays()
    {
        try
        {
            UpdateStatusDisplay();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error refreshing status displays: {ex.Message}");
        }
    }

    private InfoBarSeverity GetStatusSeverity(ManagedSaveInfo saveInfo)
    {
        return saveInfo.CurrentStatus switch
        {
            ManagedSaveInfo.SaveStatus.Clear => InfoBarSeverity.Success,
            ManagedSaveInfo.SaveStatus.Modified => InfoBarSeverity.Warning,
            ManagedSaveInfo.SaveStatus.Conflict => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    private string GetStatusTitle(ManagedSaveInfo saveInfo)
    {
        return saveInfo.CurrentStatus switch
        {
            ManagedSaveInfo.SaveStatus.Clear => "Working tree clean",
            ManagedSaveInfo.SaveStatus.Modified => "You have uncommitted changes",
            ManagedSaveInfo.SaveStatus.Conflict => "Merge conflicts detected",
            _ => "Status unknown"
        };
    }

    private string GetStatusDescription(ManagedSaveInfo saveInfo)
    {
        return saveInfo.CurrentStatus switch
        {
            ManagedSaveInfo.SaveStatus.Clear => "All changes are committed and synchronized",
            ManagedSaveInfo.SaveStatus.Modified => "Some files have been modified since the last commit",
            ManagedSaveInfo.SaveStatus.Conflict => "There are merge conflicts that need to be resolved",
            _ => "Unable to determine repository status"
        };
    }

    // Helper methods for new Current Status features
    private double CalculateSyncProgress(ManagedSaveInfo saveInfo)
    {
        // Calculate sync progress based on git status
        // For now, return 100% if working tree is clean, otherwise calculate based on staged/unstaged files
        if (saveInfo.CurrentStatus == ManagedSaveInfo.SaveStatus.Clear)
            return 100.0;

        // Mock calculation - in real implementation, this would check git status
        return 75.0;
    }

    private (string statusText, Color backgroundColor, Color borderColor, Color textColor) GetSyncStatusInfo(
        ManagedSaveInfo saveInfo)
    {
        var aheadCount = GetAheadCommitsCount(saveInfo);
        var behindCount = GetBehindCommitsCount(saveInfo);

        if (aheadCount == 0 && behindCount == 0)
            return ("Up to date",
                Color.FromArgb(255, 230, 243, 255), // Light blue background
                Color.FromArgb(255, 179, 217, 255), // Blue border
                Color.FromArgb(255, 0, 102, 204)); // Dark blue text
        if (aheadCount > 0 && behindCount == 0)
            return ($"{aheadCount} ahead",
                Color.FromArgb(255, 220, 252, 231), // Light green background
                Color.FromArgb(255, 187, 247, 208), // Green border
                Color.FromArgb(255, 22, 163, 74)); // Dark green text
        if (aheadCount == 0 && behindCount > 0)
            return ($"{behindCount} behind",
                Color.FromArgb(255, 255, 248, 197), // Light yellow background
                Color.FromArgb(255, 238, 216, 136), // Yellow border
                Color.FromArgb(255, 211, 149, 0)); // Dark yellow text
        return ($"{aheadCount} ahead, {behindCount} behind",
            Color.FromArgb(255, 254, 226, 226), // Light red background
            Color.FromArgb(255, 252, 165, 165), // Red border
            Color.FromArgb(255, 220, 38, 38)); // Dark red text
    }

    private int GetAheadCommitsCount(ManagedSaveInfo saveInfo)
    {
        // Mock implementation - in real scenario, this would query git
        return 0; // saveInfo.AheadCommits ?? 0;
    }

    private int GetBehindCommitsCount(ManagedSaveInfo saveInfo)
    {
        // Mock implementation - in real scenario, this would query git
        return 0; // saveInfo.BehindCommits ?? 0;
    }

    private async Task<(int RegionChunks, int WorldData, int PlayerData, int EntityData, int StructureData, int AddedFiles, int
        DeletedFiles, int TotalFiles)> CalculateFileBreakdownAsync(ManagedSaveInfo saveInfo)
    {
        try
        {
            if (string.IsNullOrEmpty(saveInfo.Path) || !Directory.Exists(saveInfo.Path))
            {
                return (0, 0, 0, 0, 0, 0, 0, 0);
            }

            // Use the change detection from SaveInitializationService
            var changedChunks = await _saveInitializationService.DetectChangedChunksAsync(saveInfo.Path);
            var regionChunks = changedChunks.Count;

            // Get Git status for other file types
            var gitStatus = await _gitService.GetStatusAsync(saveInfo.Path);
            var modifiedFiles = gitStatus.ModifiedFiles;
            var addedFiles = gitStatus.UntrackedFiles.Length;
            var totalFiles = modifiedFiles.Length + addedFiles;

            // Categorize files by type
            var worldDataFiles = modifiedFiles.Count(f =>
                f.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("level.dat_old", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("/data/", StringComparison.OrdinalIgnoreCase));

            var playerDataFiles = modifiedFiles.Count(f =>
                f.Contains("/playerdata/", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("/stats/", StringComparison.OrdinalIgnoreCase));

            var entityDataFiles = modifiedFiles.Count(f =>
                f.Contains("/entities/", StringComparison.OrdinalIgnoreCase));

            var structureDataFiles = modifiedFiles.Count(f =>
                f.Contains("/structures/", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("/generated/", StringComparison.OrdinalIgnoreCase));

            // For deleted files, we'd need to check git log or use more advanced git commands
            var deletedFiles = 0; // Placeholder - would need more complex git analysis

            return (
                RegionChunks: regionChunks,
                WorldData: worldDataFiles,
                PlayerData: playerDataFiles,
                EntityData: entityDataFiles,
                StructureData: structureDataFiles,
                AddedFiles: addedFiles,
                DeletedFiles: deletedFiles,
                TotalFiles: totalFiles
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error calculating file breakdown: {ex.Message}");
            // Return mock data as fallback
            return (
                RegionChunks: 0,
                WorldData: 1,
                PlayerData: 1,
                EntityData: 1,
                StructureData: 1,
                AddedFiles: 0,
                DeletedFiles: 0,
                TotalFiles: 0
            );
        }
    }

    private List<string> GetAvailableBranches(ManagedSaveInfo saveInfo)
    {
        // Mock implementation - in real scenario, this would query git branches
        return new List<string> { "main", "feature/castle-wing", "hotfix/water-fix" };
    }

    private void UpdateRemoteStatus()
    {
        var saveInfo = ViewModel.SaveInfo;
        if (saveInfo == null) return;

        if (FindName("PushStatusDisplay") is TextBlock pushStatus)
            pushStatus.Text = saveInfo.PendingPushCount > 0
                ? $"{saveInfo.PendingPushCount} commits to push"
                : "Up to date";

        if (FindName("PullStatusDisplay") is TextBlock pullStatus)
            pullStatus.Text = saveInfo.PendingPullCount > 0
                ? $"{saveInfo.PendingPullCount} commits to pull"
                : "Up to date";

        if (FindName("RemoteUrlText") is TextBlock remoteUrlText)
            remoteUrlText.Text = saveInfo.GitHubRemoteUrl ?? "Not configured";
    }

    private void ViewAllCommits_Click(object sender, RoutedEventArgs e)
    {
        // Switch to History tab to show all commits
        if (FindName("TabSelector") is SelectorBar tabSelector &&
            FindName("HistoryTab") is SelectorBarItem historyTab)
            tabSelector.SelectedItem = historyTab;
    }

    private async Task LoadSaveDetailAsync()
    {
        try
        {
            var saveId = GetNavigationParameter();
            if (!string.IsNullOrEmpty(saveId))
            {
                var saveInfo = await _managedSaveService.GetSaveByIdAsync(saveId);
                if (saveInfo != null)
                {
                    ViewModel.SaveInfo = saveInfo;
                    OnPropertyChanged(nameof(ViewModel));

                    // Update UI elements with save info
                    UpdateSaveHeader(saveInfo);
                }
            }
        }
        catch (Exception ex)
        {
            // Handle error
            Debug.WriteLine($"Error loading save detail: {ex.Message}");
        }
    }

    private string GetNavigationParameter()
    {
        return _saveId ?? string.Empty;
    }

    private void UpdateSaveHeader(ManagedSaveInfo saveInfo)
    {
        // Update header elements
        if (FindName("SaveNameText") is TextBlock saveNameText)
            saveNameText.Text = saveInfo.Name;

        if (FindName("SavePathText") is TextBlock savePathText)
            savePathText.Text = saveInfo.OriginalPath;

        // Update info line elements
        if (FindName("BranchCountText") is TextBlock branchCountText)
            branchCountText.Text = "3 branches"; // TODO: Add BranchCount property to ManagedSaveInfo

        if (FindName("CommitCountText") is TextBlock commitCountText)
            commitCountText.Text = $"{saveInfo.CommitCount} commits";

        if (FindName("LastModifiedText") is TextBlock lastModifiedText)
            lastModifiedText.Text = saveInfo.LastModifiedFormatted;

        // Update status badge
        if (FindName("SaveStatusBadgeText") is TextBlock saveStatusBadgeText)
            saveStatusBadgeText.Text = saveInfo.StatusText;

        // Update git status badges
        if (FindName("PushStatusBadge") is Border pushStatusBadge)
        {
            pushStatusBadge.Visibility = saveInfo.ShowPushBadge ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("PushStatusText") is TextBlock pushStatusText)
                pushStatusText.Text = saveInfo.PushStatusText;
        }

        if (FindName("PullStatusBadge") is Border pullStatusBadge)
        {
            pullStatusBadge.Visibility = saveInfo.ShowPullBadge ? Visibility.Visible : Visibility.Collapsed;
            if (FindName("PullStatusText") is TextBlock pullStatusText)
                pullStatusText.Text = saveInfo.PullStatusText;
        }

        // Update button states
        UpdateFetchPullButton(saveInfo.ShowPullBadge, 2); // TODO: Add PullCount property to ManagedSaveInfo
        UpdatePushButton(saveInfo.ShowPushBadge, 1); // TODO: Add PushCount property to ManagedSaveInfo

        // Note: The SaveIconFont now uses a fixed folder icon, no need to update the glyph
        // But we could change the color or other properties if needed in the future
    }

    private void TabSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem selectedItem)
        {
            var tabName = selectedItem.Name switch
            {
                "OverviewTab" => "Overview",
                "FilesTab" => "Files",
                "HistoryTab" => "History",
                "ChangesTab" => "Changes",
                "AnalyticsTab" => "Analytics",
                _ => "Overview"
            };

            NavigateToTab(tabName);
        }
    }

    private void NavigateToTab(string tabName)
    {
        try
        {
            // Hide all tab contents
            if (FindName("OverviewContent") is Grid overviewContent)
                overviewContent.Visibility = Visibility.Collapsed;
            if (FindName("FilesContent") is TextBlock filesContent)
                filesContent.Visibility = Visibility.Collapsed;
            if (FindName("HistoryContent") is TextBlock historyContent)
                historyContent.Visibility = Visibility.Collapsed;
            if (FindName("ChangesContent") is TextBlock changesContent)
                changesContent.Visibility = Visibility.Collapsed;
            if (FindName("AnalyticsContent") is TextBlock analyticsContent)
                analyticsContent.Visibility = Visibility.Collapsed;

            // Show selected tab content
            switch (tabName)
            {
                case "Overview":
                    if (FindName("OverviewContent") is Grid overview)
                    {
                        overview.Visibility = Visibility.Visible;
                        _ = LoadOverviewDataAsync();
                    }

                    break;
                case "Files":
                    if (FindName("FilesContent") is TextBlock files)
                        files.Visibility = Visibility.Visible;
                    break;
                case "History":
                    if (FindName("HistoryContent") is TextBlock history)
                        history.Visibility = Visibility.Visible;
                    break;
                case "Changes":
                    if (FindName("ChangesContent") is TextBlock changes)
                        changes.Visibility = Visibility.Visible;
                    break;
                case "Analytics":
                    if (FindName("AnalyticsContent") is TextBlock analytics)
                        analytics.Visibility = Visibility.Visible;
                    break;
            }

            ViewModel.CurrentTab = tabName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in NavigateToTab: {ex.Message}");
        }
    }

    private async void OpenInExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path) && Directory.Exists(ViewModel.SaveInfo.Path))
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(ViewModel.SaveInfo.Path);
                await Launcher.LaunchFolderAsync(folder);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening folder in explorer: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private void OpenInMinecraftButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // TODO: Implement opening save in Minecraft
            // This would typically involve launching Minecraft with the save loaded
            Debug.WriteLine("Opening save in Minecraft...");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening save in Minecraft: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // TODO: Implement git sync functionality
                // For now, just show a placeholder
                Debug.WriteLine("Syncing save...");

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error syncing save: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void CommitButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // Show progress dialog
                var progressDialog = new ContentDialog()
                {
                    Title = "Committing Changes",
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new ProgressRing { IsActive = true, Margin = new Thickness(0, 0, 0, 16) },
                            new TextBlock
                            {
                                Text = "Processing changes using partial storage...",
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false,
                    XamlRoot = XamlRoot
                };

                // Show dialog without blocking
                var dialogTask = progressDialog.ShowAsync();

                // Perform the commit using ongoing commits functionality
                var commitMessage = $"Save update - {DateTime.Now:yyyy-MM-dd HH:mm}";
                var progress = new Progress<SaveInitStep>(step =>
                {
                    // Update progress dialog content
                    if (progressDialog.Content is StackPanel panel &&
                        panel.Children.LastOrDefault() is TextBlock statusText)
                    {
                        statusText.Text = step.Message;
                    }
                });

                Debug.WriteLine("Starting ongoing commit process...");
                var success = await _saveInitializationService.CommitOngoingChangesAsync(
                    ViewModel.SaveInfo.Path,
                    commitMessage,
                    progress);

                // Close progress dialog
                progressDialog.Hide();

                if (success)
                {
                    await ShowSuccessDialogSafe("Changes Committed",
                        "Your changes have been successfully committed using partial storage.");

                    // Refresh the page data
                    await LoadSaveDetailAsync();
                    await UpdateChangeDetectionDataAsync();
                }
                else
                {
                    await ShowErrorDialogSafe("Commit Failed",
                        "There was an error committing your changes. Please check the logs for more details.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error committing changes: {ex.Message}");
            await ShowErrorDialogSafe("Error",
                $"An unexpected error occurred while committing changes: {ex.Message}");
        }
    }

    private async void PushButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // TODO: Implement git push functionality
                // For now, just show a placeholder
                Debug.WriteLine("Pushing changes...");

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error pushing changes: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private void BranchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is BranchComboBoxItem selectedItem)
            {
                if (selectedItem.IsCreateAction)
                {
                    // Handle "Create new branch" action
                    CreateBranchButton_Click(sender, new RoutedEventArgs());

                    // Reset selection to current branch to avoid showing "Create new branch" as selected
                    var currentBranchItem = ((List<BranchComboBoxItem>)comboBox.ItemsSource)
                        .FirstOrDefault(item => !item.IsSeparator && !item.IsCreateAction && item.BranchName == "main");
                    comboBox.SelectedItem = currentBranchItem;
                }
                else if (!selectedItem.IsSeparator && !string.IsNullOrEmpty(selectedItem.BranchName))
                {
                    // Handle actual branch selection
                    Debug.WriteLine($"Switching to branch: {selectedItem.BranchName}");
                    // TODO: Implement branch switching functionality
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error switching branch: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void FetchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // Set fetching state
                SetFetchingState(true);

                Debug.WriteLine("Fetching changes...");
                // TODO: Implement git fetch functionality

                // Simulate fetch operation
                await Task.Delay(2000);

                // After fetch, check if there are commits to pull and update accordingly
                UpdateFetchPullButton(true, 2); // Example: 2 commits to pull

                // Reset fetching state
                SetFetchingState(false);

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            SetFetchingState(false);
            Debug.WriteLine($"Error fetching changes: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void PullButton_Click(SplitButton sender, SplitButtonClickEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                Debug.WriteLine("Pulling changes...");
                // TODO: Implement git pull functionality

                // After pull, reset to fetch mode
                UpdateFetchPullButton(false, 0);

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error pulling changes: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private void SetFetchingState(bool isFetching)
    {
        if (FindName("FetchButton") is Button fetchButton &&
            FindName("FetchIcon") is FontIcon fetchIcon &&
            FindName("FetchText") is TextBlock fetchText)
        {
            fetchButton.IsEnabled = !isFetching;

            if (isFetching)
            {
                fetchText.Text = "Fetching...";
                // Add spinning animation to icon
                var storyboard = new Storyboard();
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(1),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                var rotateTransform = new RotateTransform();
                fetchIcon.RenderTransform = rotateTransform;
                fetchIcon.RenderTransformOrigin = new Point(0.5, 0.5);

                Storyboard.SetTarget(animation, rotateTransform);
                Storyboard.SetTargetProperty(animation, "Angle");
                storyboard.Children.Add(animation);
                storyboard.Begin();

                // Store storyboard for later cleanup
                fetchIcon.Tag = storyboard;
            }
            else
            {
                fetchText.Text = "Fetch remote";
                // Stop spinning animation
                if (fetchIcon.Tag is Storyboard storyboard)
                {
                    storyboard.Stop();
                    fetchIcon.RenderTransform = null;
                    fetchIcon.Tag = null;
                }
            }
        }
    }

    private void CreateBranchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // TODO: Implement create branch dialog and functionality
            Debug.WriteLine("Creating new branch...");

            // For now, just close the dropdown
            if (FindName("BranchComboBox") is ComboBox branchComboBox) branchComboBox.IsDropDownOpen = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating branch: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void StashButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // TODO: Implement git stash functionality
                Debug.WriteLine("Stashing changes...");

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stashing changes: {ex.Message}");
            // TODO: Show error message to user
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button scanButton)
        {
            var originalContent = scanButton.Content;
            scanButton.Content = "Scanning...";
            scanButton.IsEnabled = false;

            try
            {
                if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
                {
                    Debug.WriteLine("Scanning save files for changes...");

                    // Use our change detection system
                    var changedChunks = await _saveInitializationService.DetectChangedChunksAsync(ViewModel.SaveInfo.Path);
                    Debug.WriteLine($"Detected {changedChunks.Count} changed chunks");

                    // Update all change detection data immediately
                    await UpdateChangeDetectionDataAsync();

                    // Show results to user
                    if (changedChunks.Count > 0)
                    {
                        await ShowInfoDialogSafe("Scan Complete",
                            $"Scan complete! Found {changedChunks.Count} changed chunks that can be committed.");
                    }
                    else
                    {
                        await ShowInfoDialogSafe("Scan Complete",
                            "Scan complete! No changes detected since last commit.");
                    }

                    // Refresh save info
                    await LoadSaveDetailAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning save: {ex.Message}");
                await ShowErrorDialogSafe("Scan Error",
                    $"An error occurred while scanning for changes: {ex.Message}");
            }
            finally
            {
                scanButton.Content = originalContent;
                scanButton.IsEnabled = true;
            }
        }
    }

    private void UpdateFetchPullButton(bool hasPendingPulls, int pullCount)
    {
        if (FindName("FetchButton") is Button fetchButton &&
            FindName("PullButton") is SplitButton pullButton &&
            FindName("PullBadge") is Border pullBadge &&
            FindName("PullBadgeText") is TextBlock pullBadgeText)
        {
            if (hasPendingPulls)
            {
                // Show pull button, hide fetch button
                fetchButton.Visibility = Visibility.Collapsed;
                pullButton.Visibility = Visibility.Visible;
                pullBadge.Visibility = Visibility.Visible;
                pullBadgeText.Text = pullCount.ToString();
            }
            else
            {
                // Show fetch button, hide pull button
                fetchButton.Visibility = Visibility.Visible;
                pullButton.Visibility = Visibility.Collapsed;
                pullBadge.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdatePushButton(bool hasPendingPushes, int pushCount)
    {
        if (FindName("PushButton") is Button pushButton &&
            FindName("PushBadge") is Border pushBadge &&
            FindName("PushBadgeText") is TextBlock pushBadgeText)
        {
            if (hasPendingPushes)
            {
                pushButton.IsEnabled = true;
                pushBadge.Visibility = Visibility.Visible;
                pushBadgeText.Text = pushCount.ToString();
            }
            else
            {
                pushButton.IsEnabled = false;
                pushBadge.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void LoadBranches()
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path) &&
                FindName("BranchComboBox") is ComboBox branchComboBox)
            {
                // TODO: Get actual branches from git service
                var branches = new List<string> { "main", "develop", "feature/new-feature" };

                var comboBoxItems = new List<BranchComboBoxItem>();

                // Add actual branches
                foreach (var branch in branches)
                    comboBoxItems.Add(new BranchComboBoxItem
                    {
                        DisplayName = branch,
                        BranchName = branch,
                        IsSeparator = false,
                        IsCreateAction = false
                    });

                // Add separator
                comboBoxItems.Add(new BranchComboBoxItem
                {
                    IsSeparator = true
                });

                // Add "Create new branch" option
                comboBoxItems.Add(new BranchComboBoxItem
                {
                    DisplayName = "Create new branch",
                    IsCreateAction = true,
                    IsSeparator = false
                });

                branchComboBox.ItemsSource = comboBoxItems;

                // Set selected item to current branch (main for now)
                var currentBranchItem = comboBoxItems.FirstOrDefault(item => item.BranchName == "main");
                branchComboBox.SelectedItem = currentBranchItem;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading branches: {ex.Message}");
        }
    }

    private async void ViewOnGitHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.SaveInfo?.IsGitHubLinked == true &&
                !string.IsNullOrWhiteSpace(ViewModel.SaveInfo.GitHubRepositoryName))
            {
                var repoName = ViewModel.SaveInfo.GitHubRepositoryName;
                var user = await _gitHubAppsService.GetUserAsync(_configurationService.GitHubAccessToken);
                if (user?.Login != null)
                {
                    var githubUrl = $"https://github.com/{user.Login}/{repoName}";
                    await Launcher.LaunchUriAsync(new Uri(githubUrl));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening GitHub repository: {ex.Message}");
            await ShowErrorDialogSafe("Error",
                "Unable to open the GitHub repository. Please check your internet connection and try again.");
        }
    }

    #region Change Detection Updates

    /// <summary>
    /// Start periodic updates for change detection (every 30 seconds)
    /// </summary>
    private void StartChangeDetectionUpdates()
    {
        // Stop any existing timer
        StopChangeDetectionUpdates();

        // Create a timer that updates every 30 seconds
        _changeDetectionTimer = new Timer(_ =>
        {
            try
            {
                // Update on the UI thread
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await UpdateChangeDetectionDataAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in change detection timer: {ex.Message}");
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Stop periodic change detection updates
    /// </summary>
    private void StopChangeDetectionUpdates()
    {
        _changeDetectionTimer?.Dispose();
        _changeDetectionTimer = null;
    }

    /// <summary>
    /// Update all change detection data
    /// </summary>
    private async Task UpdateChangeDetectionDataAsync()
    {
        if (ViewModel.SaveInfo == null) return;

        try
        {
            // Update file changes summary with real data
            UpdateFileChangesSummary();

            // Update sync progress and status
            await UpdateSyncProgressAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating change detection data: {ex.Message}");
        }
    }

    /// <summary>
    /// Update sync progress with real git status
    /// </summary>
    private async Task UpdateSyncProgressAsync()
    {
        if (ViewModel.SaveInfo == null || string.IsNullOrEmpty(ViewModel.SaveInfo.Path)) return;

        try
        {
            var gitStatus = await _gitService.GetStatusAsync(ViewModel.SaveInfo.Path);
            var totalChangedFiles = gitStatus.ModifiedFiles.Length + gitStatus.UntrackedFiles.Length;

            // Update ManagedSaveInfo with real data
            ViewModel.SaveInfo.HasPendingChanges = totalChangedFiles > 0;

            // Update current status based on changes
            if (totalChangedFiles > 0)
            {
                ViewModel.SaveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Modified;
            }
            else
            {
                ViewModel.SaveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Clear;
            }

            // Update UI displays
            UpdateSyncStatusBadge();
            UpdateStatusInfoBar();
            UpdateSyncProgressDisplay();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating sync progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Manual refresh button for immediate update
    /// </summary>
    private async void RefreshChangeDetection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button refreshButton)
        {
            var originalContent = refreshButton.Content;
            refreshButton.Content = "Refreshing...";
            refreshButton.IsEnabled = false;

            try
            {
                await UpdateChangeDetectionDataAsync();
            }
            finally
            {
                refreshButton.Content = originalContent;
                refreshButton.IsEnabled = true;
            }
        }
    }

    #endregion

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // Clean up timer when leaving the page
        StopChangeDetectionUpdates();
    }

    private void OnPropertyChanged(string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
