using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using fNbt;
using GitMC.Constants;
using GitMC.Extensions;
using GitMC.Models;
using GitMC.Models.GitHub;
using GitMC.Services;
using GitMC.Utils.Nbt;
using GitMC.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace GitMC.Views
{
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
        private string _originalFileContent = string.Empty;

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

                // Show manual Translate action when there are uncommitted changes
                if (FindName("TranslateButton") is Button tacButton)
                {
                    tacButton.Visibility = ViewModel.SaveInfo.CurrentStatus == ManagedSaveInfo.SaveStatus.Modified
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    tacButton.IsEnabled = !ViewModel.IsCommitInProgress;
                }
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
                        await RecomputeCanTranslateAsync();
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
                    "SettingsTab" => "Settings",
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
                if (FindName("OverviewScrollViewer") is ScrollViewer overviewScrollViewer)
                    overviewScrollViewer.Visibility = Visibility.Collapsed;
                if (FindName("FilesContent") is TextBlock filesContent)
                    filesContent.Visibility = Visibility.Collapsed;
                if (FindName("HistoryContent") is TextBlock historyContent)
                    historyContent.Visibility = Visibility.Collapsed;
                if (FindName("ChangesContent") is Grid changesContent)
                    changesContent.Visibility = Visibility.Collapsed;
                if (FindName("AnalyticsContent") is TextBlock analyticsContent)
                    analyticsContent.Visibility = Visibility.Collapsed;
                if (FindName("SettingsContent") is Grid settingsContent)
                    settingsContent.Visibility = Visibility.Collapsed;

                // Show selected tab content
                switch (tabName)
                {
                    case "Overview":
                        if (FindName("OverviewScrollViewer") is ScrollViewer overview)
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
                        if (FindName("ChangesContent") is Grid changes)
                        {
                            changes.Visibility = Visibility.Visible;
                            _ = LoadChangedFilesDataAsync();
                        }
                        break;
                    case "Analytics":
                        if (FindName("AnalyticsContent") is TextBlock analytics)
                            analytics.Visibility = Visibility.Visible;
                        break;
                    case "Settings":
                        if (FindName("SettingsContent") is Grid settings)
                            settings.Visibility = Visibility.Visible;
                        break;
                }

                ViewModel.CurrentTab = tabName;
                _ = RecomputeCanTranslateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigateToTab: {ex.Message}");
            }
        }

        private bool _autoCommitInProgress;

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SaveInfo?.Path == null || _autoCommitInProgress) return;

            try
            {
                ViewModel.IsCommitInProgress = true;
                _autoCommitInProgress = true;

                // Wire up translation progress to the Translation Status UI
                if (FindName("TranslationStepText") is TextBlock stepText &&
                    FindName("TranslationProgressBar") is ProgressBar pb &&
                    FindName("TranslationProgressText") is TextBlock pct)
                {
                    stepText.Text = "Starting translation...";
                    pb.Value = 0;
                    pct.Text = "0%";
                }

                var progress = new Progress<SaveInitStep>(step =>
                {
                    Debug.WriteLine($"Translate: {step.Message}");
                    if (FindName("TranslationStepText") is TextBlock s) s.Text = step.Message ?? string.Empty;
                    if (FindName("TranslationProgressBar") is ProgressBar p && step.TotalProgress > 0)
                    {
                        var value = Math.Min(100, Math.Max(0, (double)step.CurrentProgress / step.TotalProgress * 100.0));
                        p.Value = value;
                    }
                    if (FindName("TranslationProgressText") is TextBlock t && step.TotalProgress > 0)
                    {
                        var value = Math.Min(100, Math.Max(0, (double)step.CurrentProgress / step.TotalProgress * 100.0));
                        t.Text = $"{value:F0}%";
                    }
                });

                var ok = await _saveInitializationService.TranslateChangedAsync(ViewModel.SaveInfo.Path, progress);

                if (ok)
                {
                    await LoadSaveDetailAsync();
                    await UpdateChangeDetectionDataAsync();
                    if (FindName("TranslationStepText") is TextBlock s) s.Text = "Translation complete";
                    await RecomputeCanTranslateAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranslateButton_Click error: {ex.Message}");
            }
            finally
            {
                ViewModel.IsCommitInProgress = false;
                _autoCommitInProgress = false;
                UpdateStatusInfoBar();
                await RecomputeCanTranslateAsync();
            }
        }

        private async void AutoTranslateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.SaveInfo != null)
                {
                    await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to persist AutoTranslateOnIdle: {ex.Message}");
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

                        // Auto-translate when enabled and idle (no locks)
                        if (ViewModel.SaveInfo?.AutoTranslateOnIdle == true &&
                            ViewModel.SaveInfo.CurrentStatus == ManagedSaveInfo.SaveStatus.Modified &&
                            !_autoCommitInProgress && !ViewModel.IsCommitInProgress)
                        {
                            var inUse = await IsSaveInUseAsync(ViewModel.SaveInfo.Path);
                            if (!inUse)
                            {
                                TranslateButton_Click(this, new RoutedEventArgs());
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in change detection timer: {ex.Message}");
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        private async Task<bool> IsSaveInUseAsync(string savePath)
        {
            try
            {
                // Simple heuristic: try to open common files exclusively to detect locks
                var candidates = new[]
                {
                    System.IO.Path.Combine(savePath, "level.dat"),
                    System.IO.Path.Combine(savePath, "region"),
                    System.IO.Path.Combine(savePath, "region2"),
                };

                foreach (var candidate in candidates)
                {
                    if (Directory.Exists(candidate))
                    {
                        var mca = Directory.EnumerateFiles(candidate, "*.mca").FirstOrDefault();
                        if (mca != null && IsFileLocked(mca)) return true;
                    }
                    else if (File.Exists(candidate) && IsFileLocked(candidate))
                    {
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            await Task.CompletedTask;
            return false;
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch
            {
                return true;
            }
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
                await RecomputeCanTranslateAsync();
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

        #region Changes Tab Methods

        /// <summary>
        /// Load changed files data for the Changes tab
        /// </summary>
        private async Task LoadChangedFilesDataAsync()
        {
            if (ViewModel.SaveInfo?.Path == null) return;

            try
            {
                // Get changed chunks using our detection service
                var changedChunks = await _saveInitializationService.DetectChangedChunksAsync(ViewModel.SaveInfo.Path);
                var gitStatus = await _gitService.GetStatusAsync(ViewModel.SaveInfo.Path);

                // Group and categorize files
                var fileGroups = new Dictionary<FileCategory, ChangedFileGroup>();

                // Process .mca files (region files)
                var regionFiles = gitStatus.ModifiedFiles
                    .Where(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (regionFiles.Any())
                {
                    var regionGroup = GetOrCreateGroup(fileGroups, FileCategory.Region, "Region Files", "\uE8B7");

                    foreach (var regionFile in regionFiles)
                    {
                        var fullPath = Path.Combine(ViewModel.SaveInfo.Path, regionFile);
                        var fileInfo = new FileInfo(fullPath);

                        var changedFile = new ChangedFile
                        {
                            FileName = Path.GetFileName(regionFile),
                            RelativePath = regionFile,
                            FullPath = fullPath,
                            Status = ChangeStatus.Modified,
                            Category = FileCategory.Region,
                            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                            LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                            DisplaySize = FormatFileSize(fileInfo.Exists ? fileInfo.Length : 0),
                            StatusText = "Modified",
                            CategoryText = "Region",
                            IconGlyph = "\uE8B7",
                            StatusColor = "#FF9800",
                            ChunkCount = changedChunks.Count(c => c.Contains(Path.GetFileNameWithoutExtension(regionFile)))
                        };

                        // Determine translation state (one translation, then use)
                        TryPopulateTranslationInfo(changedFile);

                        regionGroup.Files.Add(changedFile);
                    }
                }

                // Process other modified files
                var otherFiles = gitStatus.ModifiedFiles
                    .Where(f => !f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in otherFiles)
                {
                    var category = CategorizeFile(file);
                    var group = GetOrCreateGroup(fileGroups, category.Category, category.Name, category.Icon);

                    var fullPath = Path.Combine(ViewModel.SaveInfo.Path, file);
                    var fileInfo = new FileInfo(fullPath);

                    var changedFile = new ChangedFile
                    {
                        FileName = Path.GetFileName(file),
                        RelativePath = file,
                        FullPath = fullPath,
                        Status = ChangeStatus.Modified,
                        Category = category.Category,
                        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                        DisplaySize = FormatFileSize(fileInfo.Exists ? fileInfo.Length : 0),
                        StatusText = "Modified",
                        CategoryText = category.Name,
                        IconGlyph = category.Icon,
                        StatusColor = "#FF9800"
                    };

                    TryPopulateTranslationInfo(changedFile);

                    group.Files.Add(changedFile);
                }

                // Process untracked files
                foreach (var file in gitStatus.UntrackedFiles)
                {
                    var category = CategorizeFile(file);
                    var group = GetOrCreateGroup(fileGroups, category.Category, category.Name, category.Icon);

                    var fullPath = Path.Combine(ViewModel.SaveInfo.Path, file);
                    var fileInfo = new FileInfo(fullPath);

                    var changedFile = new ChangedFile
                    {
                        FileName = Path.GetFileName(file),
                        RelativePath = file,
                        FullPath = fullPath,
                        Status = ChangeStatus.Added,
                        Category = category.Category,
                        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                        DisplaySize = FormatFileSize(fileInfo.Exists ? fileInfo.Length : 0),
                        StatusText = "Added",
                        CategoryText = category.Name,
                        IconGlyph = category.Icon,
                        StatusColor = "#4CAF50"
                    };

                    TryPopulateTranslationInfo(changedFile);

                    group.Files.Add(changedFile);
                }

                // Update counts and sizes for each group
                foreach (var group in fileGroups.Values)
                {
                    group.FileCount = group.Files.Count;
                    group.TotalSizeBytes = group.Files.Sum(f => f.FileSizeBytes);
                    group.DisplaySize = FormatFileSize(group.TotalSizeBytes);
                }

                // Update ViewModel
                ViewModel.ChangedFileGroups.Clear();
                foreach (var group in fileGroups.Values.OrderBy(g => g.Category))
                {
                    ViewModel.ChangedFileGroups.Add(group);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading changed files: {ex.Message}");
            }
        }

        private ChangedFileGroup GetOrCreateGroup(Dictionary<FileCategory, ChangedFileGroup> groups, FileCategory category, string name, string icon)
        {
            if (!groups.TryGetValue(category, out var group))
            {
                group = new ChangedFileGroup
                {
                    Category = category,
                    CategoryName = name,
                    CategoryIcon = icon,
                    Files = new ObservableCollection<ChangedFile>()
                };
                groups[category] = group;
            }
            return group;
        }

        private (FileCategory Category, string Name, string Icon) CategorizeFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            var directory = Path.GetDirectoryName(filePath)?.ToLowerInvariant() ?? "";

            // Region files
            if (fileName.EndsWith(".mca") || fileName.EndsWith(".mcc"))
                return (FileCategory.Region, "Region Files", "\uE8B7");

            // Data files
            if (fileName.Contains("level.dat") || directory.Contains("data"))
                return (FileCategory.Data, "Data Files", "\uE8A5");

            // Entity files
            if (directory.Contains("entities") || directory.Contains("playerdata"))
                return (FileCategory.Entity, "Entity Files", "\uE77B");

            // Mod files
            if (directory.Contains("mods") || fileName.EndsWith(".jar") || fileName.EndsWith(".cfg"))
                return (FileCategory.Mod, "Mod Files", "\uE8B9");

            // Default to other
            return (FileCategory.Other, "Other Files", "\uE8A5");
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";

            string[] sizes = { "B", "KB", "MB", "GB" };
            var order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Refresh changes button click handler
        /// </summary>
        private async void RefreshChanges_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button refreshButton)
            {
                var originalContent = refreshButton.Content;
                refreshButton.IsEnabled = false;

                // Show loading state
                refreshButton.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new ProgressRing { Width = 14, Height = 14, IsActive = true },
                        new TextBlock { Text = "Refreshing...", VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                try
                {
                    await LoadChangedFilesDataAsync();
                }
                finally
                {
                    refreshButton.Content = originalContent;
                    refreshButton.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Handle file selection in the changed files list
        /// </summary>
        private void ChangedFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListView listView && listView.SelectedItem is ChangedFile selectedFile)
            {
                ViewModel.SelectedChangedFile = selectedFile;
                _ = LoadSelectedFileEditorAsync();
            }
        }

        /// <summary>
        /// Commit changes button click handler
        /// </summary>
        private async void CommitChanges_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SaveInfo?.Path == null || string.IsNullOrWhiteSpace(ViewModel.CommitMessage))
            {
                var dialog = new ContentDialog
                {
                    Title = "Commit Error",
                    Content = "Please enter a commit message before committing changes.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            ViewModel.IsCommitInProgress = true;

            try
            {
                var progress = new Progress<SaveInitStep>(step =>
                {
                    // Could show progress in UI if needed
                    Debug.WriteLine($"Commit progress: {step.Message}");
                });

                var commitMessage = ViewModel.CommitMessage;
                if (!string.IsNullOrWhiteSpace(ViewModel.CommitDescription))
                {
                    commitMessage += "\n\n" + ViewModel.CommitDescription;
                }

                var success = await _saveInitializationService.CommitOngoingChangesAsync(
                    ViewModel.SaveInfo.Path,
                    commitMessage,
                    progress);

                if (success)
                {
                    // Clear commit form
                    ViewModel.CommitMessage = "";
                    ViewModel.CommitDescription = "";

                    // Refresh changed files
                    await LoadChangedFilesDataAsync();

                    // Show success message
                    var successDialog = new ContentDialog
                    {
                        Title = "Commit Successful",
                        Content = "Your changes have been committed successfully.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await successDialog.ShowAsync();
                }
                else
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Commit Failed",
                        Content = "Failed to commit changes. Please check the logs for more details.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error committing changes: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "Commit Error",
                    Content = $"An error occurred while committing: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                ViewModel.IsCommitInProgress = false;
            }
        }

        /// <summary>
        /// Commit and Push changes button click handler
        /// </summary>
        private async void CommitAndPushChanges_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SaveInfo?.Path == null || string.IsNullOrWhiteSpace(ViewModel.CommitMessage))
            {
                var dialog = new ContentDialog
                {
                    Title = "Commit Error",
                    Content = "Please enter a commit message before committing changes.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            ViewModel.IsCommitInProgress = true;

            try
            {
                var progress = new Progress<SaveInitStep>(step =>
                {
                    // Could show progress in UI if needed
                    Debug.WriteLine($"Commit & Push progress: {step.Message}");
                });

                var commitMessage = ViewModel.CommitMessage;
                if (!string.IsNullOrWhiteSpace(ViewModel.CommitDescription))
                {
                    commitMessage += "\n\n" + ViewModel.CommitDescription;
                }

                // First commit the changes
                var commitSuccess = await _saveInitializationService.CommitOngoingChangesAsync(
                    ViewModel.SaveInfo.Path,
                    commitMessage,
                    progress);

                if (commitSuccess)
                {
                    // Clear commit form after successful commit
                    ViewModel.CommitMessage = "";
                    ViewModel.CommitDescription = "";

                    // Then try to push
                    var gitMcPath = Path.Combine(ViewModel.SaveInfo.Path, "GitMC");
                    var pushSuccess = await _gitService.PushAsync(null, null, gitMcPath);

                    if (pushSuccess)
                    {
                        // Refresh changed files
                        await LoadChangedFilesDataAsync();

                        // Show success message
                        var successDialog = new ContentDialog
                        {
                            Title = "Commit & Push Successful",
                            Content = "Your changes have been committed and pushed successfully.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await successDialog.ShowAsync();
                    }
                    else
                    {
                        // Refresh changed files even if push failed
                        await LoadChangedFilesDataAsync();

                        // Show warning - commit succeeded but push failed
                        var warningDialog = new ContentDialog
                        {
                            Title = "Commit Successful, Push Failed",
                            Content = "Your changes were committed successfully, but failed to push to the remote repository. You can try pushing manually later.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await warningDialog.ShowAsync();
                    }
                }
                else
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Commit Failed",
                        Content = "Failed to commit changes. Please check the logs for more details.",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error committing and pushing changes: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "Commit & Push Error",
                    Content = $"An error occurred while committing and pushing: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                ViewModel.IsCommitInProgress = false;
            }
        }

        /// <summary>
        /// Open side panel button click handler (placeholder)
        /// </summary>
        private void OpenSidePanel_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement side panel opening logic
            Debug.WriteLine("Side panel opening - to be implemented");
        }

        /// <summary>
        /// Toggle commit section collapse/expand
        /// </summary>
        private void ToggleCommitSection_Click(object sender, RoutedEventArgs e)
        {
            var isCollapsed = CommitFormContent.Visibility == Visibility.Collapsed;

            if (isCollapsed)
            {
                // Expand the commit section
                CommitFormContent.Visibility = Visibility.Visible;
                CommitActionsContent.Visibility = Visibility.Visible;

                // Rotate the icon to point up (expanded state)
                CommitSectionCollapseIconTransform.Angle = 180;
            }
            else
            {
                // Collapse the commit section
                CommitFormContent.Visibility = Visibility.Collapsed;
                CommitActionsContent.Visibility = Visibility.Collapsed;

                // Rotate the icon to point down (collapsed state)
                CommitSectionCollapseIconTransform.Angle = 0;
            }
        }

        #endregion

        #region Editor helpers and handlers (Changes tab)

        private string GetGitMcFolder()
        {
            var basePath = ViewModel.SaveInfo?.Path ?? string.Empty;
            return string.IsNullOrEmpty(basePath) ? string.Empty : Path.Combine(basePath, "GitMC");
        }

        private string GetSnbtPathForRelative(string relativePath)
        {
            // Simple mapping: GitMC/<relative>.snbt
            var gitMc = GetGitMcFolder();
            var normalizedRel = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(gitMc, normalizedRel) + ".snbt";
        }

        private string? GetAnyChunkSnbtForMca(string mcaRelativePath)
        {
            try
            {
                // mcaRelativePath like "region/r.x.z.mca"
                var fileName = Path.GetFileNameWithoutExtension(mcaRelativePath);
                var parts = fileName.Split('.');
                if (parts.Length >= 3 && parts[0] == "r" && int.TryParse(parts[1], out var rx) && int.TryParse(parts[2], out var rz))
                {
                    var dir = Path.Combine(GetGitMcFolder(), "region", $"r.{rx}.{rz}.mca");
                    if (Directory.Exists(dir))
                    {
                        var first = Directory.EnumerateFiles(dir, "chunk_*_*.snbt").FirstOrDefault();
                        return first;
                    }
                }
            }
            catch { }
            return null;
        }

        private void TryPopulateTranslationInfo(ChangedFile file)
        {
            try
            {
                string? snbtPath = null;
                var ext = Path.GetExtension(file.FullPath).ToLowerInvariant();
                if (ext == ".mca")
                {
                    // For region files, look for any chunk SNBT under GitMC/region/r.x.z.mca
                    snbtPath = GetAnyChunkSnbtForMca(file.RelativePath);
                }
                else
                {
                    snbtPath = GetSnbtPathForRelative(file.RelativePath);
                }

                bool isTranslated = false;
                if (!string.IsNullOrEmpty(snbtPath))
                {
                    if (ext == ".mca")
                        isTranslated = File.Exists(snbtPath);
                    else
                        isTranslated = File.Exists(snbtPath) || File.Exists(snbtPath + ".chunk_mode") || Directory.Exists(Path.ChangeExtension(snbtPath, ".chunks"));
                }

                file.IsTranslated = isTranslated;
                file.SnbtPath = isTranslated ? snbtPath : null;

                // Determine if original file is directly editable (text-based)
                // Common editable text formats
                var directEditable = ext is ".txt" or ".json" or ".md" or ".log" or ".mcfunction" or ".mcmeta" or ".cfg" or ".ini" or ".csv" or ".yaml" or ".yml" or ".toml" or ".xml" or ".properties" or ".snbt";
                file.IsDirectEditable = directEditable;

                // Whitelist-based translatability with light sniffing
                file.IsTranslatable = IsKnownTranslatable(file.FullPath);

                // Decide the effective editor path and editability
                if (file.IsTranslated && !string.IsNullOrEmpty(file.SnbtPath))
                {
                    file.EditorPath = file.SnbtPath;
                }
                else if (file.IsDirectEditable)
                {
                    // For direct editable types, point editor to the original file path
                    file.EditorPath = file.FullPath;
                }
                else
                {
                    file.EditorPath = null;
                }
            }
            catch { /* ignore mapping errors */ }
        }

        // Whitelist + magic sniff helpers
        private static bool HasKnownTranslatablePattern(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/').ToLowerInvariant() ?? string.Empty;

            // Region files
            if (name.EndsWith(".mca") || name.EndsWith(".mcc")) return true;

            // Common NBT-bearing files/folders
            if (name == "level.dat") return true;
            if (dir.Contains("/playerdata") || dir.Contains("/entities") || dir.Contains("/data"))
            {
                // Common extensions that are likely NBT
                if (name.EndsWith(".dat") || name.EndsWith(".mcr") || name.EndsWith(".nbt")) return true;
            }

            return false;
        }

        private static bool IsLikelyNbtByMagic(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < 3) return false;
                Span<byte> header = stackalloc byte[3];
                var read = fs.Read(header);
                if (read < 3) return false;
                // GZip header: 1F 8B
                if (header[0] == 0x1F && header[1] == 0x8B) return true;
                // NBT uncompressed typically starts with a tag type (0-12). We check for plausible range
                if (header[0] <= 0x0C) return true;
            }
            catch { }
            return false;
        }

        private static bool IsKnownTranslatable(string path)
        {
            if (!File.Exists(path)) return false;
            if (HasKnownTranslatablePattern(path)) return true;
            // If extensionless or unknown but inside known dirs, sniff
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || ext == ".dat" || ext == ".nbt" || ext == ".mcr")
                return IsLikelyNbtByMagic(path);
            return false;
        }

        private async Task RecomputeCanTranslateAsync()
        {
            try
            {
                var can = false;
                var info = ViewModel.SaveInfo;
                if (info?.Path != null && info.CurrentStatus == ManagedSaveInfo.SaveStatus.Modified && !ViewModel.IsCommitInProgress && !_autoCommitInProgress)
                {
                    var inUse = await IsSaveInUseAsync(info.Path);
                    if (!inUse)
                    {
                        var status = await _gitService.GetStatusAsync(info.Path);
                        var changed = status.ModifiedFiles.Concat(status.UntrackedFiles);
                        foreach (var rel in changed)
                        {
                            var full = Path.Combine(info.Path, rel);
                            // Skip direct-editable
                            var ext = Path.GetExtension(full).ToLowerInvariant();
                            var directEditable = ext is ".txt" or ".json" or ".md" or ".log" or ".mcfunction" or ".mcmeta" or ".cfg" or ".ini" or ".csv" or ".yaml" or ".yml" or ".toml" or ".xml" or ".properties" or ".snbt";
                            if (directEditable) continue;

                            if (IsKnownTranslatable(full)) { can = true; break; }
                        }
                    }
                }
                ViewModel.CanTranslate = can;
            }
            catch { ViewModel.CanTranslate = false; }
        }

        private async Task LoadSelectedFileEditorAsync()
        {
            try
            {
                ViewModel.ValidationStatus = null;
                var editorPath = ViewModel.SelectedChangedFile?.EditorPath;
                // If MCA selected and not translated, show a hint text instead of empty editor
                if (ViewModel.SelectedChangedFile is { } sel && string.IsNullOrEmpty(editorPath))
                {
                    var ext = Path.GetExtension(sel.FullPath).ToLowerInvariant();
                    if (ext == ".mca")
                    {
                        _originalFileContent = string.Empty;
                        ViewModel.FileContent = "This region file is not translated yet. Click 'Translate' in the InfoBar or 'Force Translation' in Tools to generate SNBT for viewing.";
                        ViewModel.IsFileModified = false;
                        UpdateEditorMetrics();
                        return;
                    }
                }
                if (!string.IsNullOrEmpty(editorPath))
                {
                    // Normalize to absolute
                    var pathToLoad = Path.IsPathRooted(editorPath)
                        ? editorPath
                        : Path.Combine(ViewModel.SaveInfo?.Path ?? string.Empty, editorPath);

                    if (!File.Exists(pathToLoad))
                    {
                        // If not found, clear content and return
                        _originalFileContent = string.Empty;
                        ViewModel.FileContent = string.Empty;
                        ViewModel.IsFileModified = false;
                        UpdateEditorMetrics();
                        return;
                    }

                    var text = await File.ReadAllTextAsync(pathToLoad);
                    _originalFileContent = text;
                    ViewModel.FileContent = text;
                    ViewModel.IsFileModified = false;
                    UpdateEditorMetrics();

                    // Apply font settings to editor
                    if (FindName("FileTextEditor") is TextBox editor)
                    {
                        editor.FontFamily = new FontFamily(ViewModel.SelectedFontFamily);
                        editor.FontSize = ViewModel.EditorFontSize;
                        editor.TextWrapping = ViewModel.IsWordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    }
                }
                else
                {
                    _originalFileContent = string.Empty;
                    ViewModel.FileContent = string.Empty;
                    ViewModel.IsFileModified = false;
                    UpdateEditorMetrics();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading selected file editor: {ex.Message}");
            }
        }

        private void UpdateEditorMetrics()
        {
            var text = ViewModel.FileContent ?? string.Empty;
            var lineCount = 1 + text.Count(c => c == '\n');
            ViewModel.EditorLineCount = $"Lines: {lineCount}";
            ViewModel.EditorCharCount = $"Chars: {text.Length}";

            if (FindName("FileTextEditor") is TextBox editor)
            {
                var (line, col) = GetLineAndColumn(text, editor.SelectionStart);
                ViewModel.CurrentCursorPosition = $"Ln {line}, Col {col}";
            }
            else
            {
                ViewModel.CurrentCursorPosition = "";
            }
        }

        private static (int line, int col) GetLineAndColumn(string text, int index)
        {
            if (index < 0) index = 0;
            if (index > text.Length) index = text.Length;
            int line = 1, col = 1;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n') { line++; col = 1; }
                else col++;
            }
            return (line, col);
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Keep ViewModel.FileContent in sync via x:Bind TwoWay; just update metrics and modified state
            try
            {
                ViewModel.IsFileModified = !string.Equals(ViewModel.FileContent ?? string.Empty, _originalFileContent,
                    StringComparison.Ordinal);
                UpdateEditorMetrics();
            }
            catch { }
        }

        private void TextEditor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateEditorMetrics();
        }

        private void ToggleSidePanel_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsSidePanelVisible = !ViewModel.IsSidePanelVisible;
            try
            {
                // Toggle the column width immediately for snappy UI and to avoid empty space
                if (FindName("SidePanelColumn") is ColumnDefinition col)
                    col.Width = new GridLength(ViewModel.IsSidePanelVisible ? 280 : 0);
                if (FindName("SidePanelToggleIcon") is FontIcon icon)
                    icon.Glyph = ViewModel.IsSidePanelVisible ? "\uE76B" : "\uE76F";
            }
            catch { }
        }

        private async void ValidateContent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = ViewModel.FileContent ?? string.Empty;
                if (ViewModel.IsJsonContext)
                {
                    try
                    {
                        await Task.Run(() => JsonDocument.Parse(content, new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        }));
                        ViewModel.ValidationStatus = new ValidationStatusModel
                        {
                            Message = "Valid JSON",
                            Icon = "\uE73E",
                            BackgroundColor = "#E6F4EA",
                            ForegroundColor = "#1E7E34"
                        };
                    }
                    catch (Exception ex)
                    {
                        ViewModel.ValidationStatus = new ValidationStatusModel
                        {
                            Message = $"Invalid JSON: {ex.Message}",
                            Icon = "\uEA39",
                            BackgroundColor = "#FCE8E6",
                            ForegroundColor = "#B00020"
                        };
                    }
                }
                else // SNBT
                {
                    try
                    {
                        await Task.Run(() => SnbtParser.Parse(content, false));
                        ViewModel.ValidationStatus = new ValidationStatusModel
                        {
                            Message = "Valid SNBT",
                            Icon = "\uE73E",
                            BackgroundColor = "#E6F4EA",
                            ForegroundColor = "#1E7E34"
                        };
                    }
                    catch (Exception ex)
                    {
                        ViewModel.ValidationStatus = new ValidationStatusModel
                        {
                            Message = $"Invalid SNBT: {ex.Message}",
                            Icon = "\uEA39",
                            BackgroundColor = "#FCE8E6",
                            ForegroundColor = "#B00020"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Validate error: {ex.Message}");
            }
        }

        private async void SaveFileChanges_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.SelectedChangedFile?.EditorPath is string editorPath)
                {
                    var pathToSave = Path.IsPathRooted(editorPath)
                        ? editorPath
                        : Path.Combine(ViewModel.SaveInfo?.Path ?? string.Empty, editorPath);
                    await File.WriteAllTextAsync(pathToSave, ViewModel.FileContent ?? string.Empty);
                    _originalFileContent = ViewModel.FileContent ?? string.Empty;
                    ViewModel.IsFileModified = false;

                    ViewModel.ValidationStatus = new ValidationStatusModel
                    {
                        Message = "Saved",
                        Icon = "\uE74E",
                        BackgroundColor = "#E3F2FD",
                        ForegroundColor = "#1565C0"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save error: {ex.Message}");
                ViewModel.ValidationStatus = new ValidationStatusModel
                {
                    Message = $"Save failed: {ex.Message}",
                    Icon = "\uEA39",
                    BackgroundColor = "#FCE8E6",
                    ForegroundColor = "#B00020"
                };
            }
        }

        private async void ForceTranslation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.SelectedChangedFile is not { } file || ViewModel.SaveInfo?.Path == null) return;

                // Block when world/files are in use
                var inUse = await IsSaveInUseAsync(ViewModel.SaveInfo.Path);
                if (inUse)
                {
                    await ShowErrorDialogSafe("World Busy", "The world appears to be in use. Close Minecraft or any tools locking files and try again.");
                    return;
                }

                // Only allow for known translatable types
                var ext = Path.GetExtension(file.FullPath).ToLowerInvariant();
                if (!file.IsTranslatable && ext != ".mca")
                {
                    await ShowErrorDialogSafe("Unsupported", "This file type isn't supported for translation. You can open it directly if it's a text-based file.");
                    return;
                }

                if (ext == ".mca")
                {
                    // Trigger translation for changed chunks in the save; simplest and consistent
                    var stepProgress = new Progress<SaveInitStep>(s => Debug.WriteLine($"Translate: {s.Message}"));
                    await _saveInitializationService.TranslateChangedAsync(ViewModel.SaveInfo.Path, stepProgress);
                }
                else
                {
                    var snbtPath = GetSnbtPathForRelative(file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(snbtPath)!);
                    var progress = new Progress<string>(msg => Debug.WriteLine($"Translation: {msg}"));
                    await _nbtService.ConvertToSnbtAsync(file.FullPath, snbtPath, progress);
                }

                // Refresh translation state and reload
                TryPopulateTranslationInfo(file);
                // Update toggles based on new editor context
                ViewModel.CanForceTranslation = !file.IsTranslated && !file.IsDirectEditable;
                var path = file.SnbtPath ?? file.EditorPath ?? file.FullPath;
                var newExt = string.IsNullOrEmpty(path) ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
                ViewModel.IsSnbtContext = newExt == ".snbt";
                ViewModel.IsJsonContext = newExt == ".json";
                ViewModel.CanValidate = ViewModel.IsSnbtContext || ViewModel.IsJsonContext;
                ViewModel.CanFormat = ViewModel.CanValidate;
                ViewModel.CanMinify = ViewModel.CanValidate;
                await LoadSelectedFileEditorAsync();
                await RecomputeCanTranslateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Force translation failed: {ex.Message}");
                await ShowErrorDialogSafe("Translation Error", ex.Message);
            }
        }

        private void WordWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (FindName("FileTextEditor") is TextBox editor)
                editor.TextWrapping = ViewModel.IsWordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is string family)
            {
                if (!string.Equals(ViewModel.SelectedFontFamily, family, StringComparison.Ordinal))
                {
                    ViewModel.SelectedFontFamily = family;
                    if (FindName("FileTextEditor") is TextBox editor)
                        editor.FontFamily = new FontFamily(family);
                }
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (FindName("FileTextEditor") is TextBox editor)
                editor.FontSize = ViewModel.EditorFontSize;
        }

        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is double d)
            {
                if (Math.Abs(ViewModel.EditorFontSize - d) > double.Epsilon)
                {
                    ViewModel.EditorFontSize = d;
                    if (FindName("FileTextEditor") is TextBox editor)
                        editor.FontSize = d;
                }
            }
        }

        private async void ReloadFromDisk_Click(object sender, RoutedEventArgs e)
        {
            await LoadSelectedFileEditorAsync();
        }

        private async void ExportFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel.SelectedChangedFile?.EditorPath is not string editorPath) return;
                var picker = new FileSavePicker();
                var abs = Path.IsPathRooted(editorPath) ? editorPath : Path.Combine(ViewModel.SaveInfo?.Path ?? string.Empty, editorPath);
                var ext = Path.GetExtension(abs);
                var name = Path.GetFileName(abs);
                // Offer a reasonable file type choice based on extension
                var label = string.IsNullOrEmpty(ext) ? "Text File" : $"{ext.ToUpperInvariant()} File";
                var choice = string.IsNullOrEmpty(ext) ? new List<string> { ".txt" } : new List<string> { ext };
                picker.FileTypeChoices.Add(label, choice);
                picker.SuggestedFileName = name;

                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, ViewModel.FileContent ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogSafe("Export Error", ex.Message);
            }
        }

        private async void ImportFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".snbt");
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".md");
                picker.FileTypeFilter.Add(".log");
                picker.FileTypeFilter.Add(".mcfunction");
                picker.FileTypeFilter.Add(".mcmeta");
                picker.FileTypeFilter.Add(".cfg");
                picker.FileTypeFilter.Add(".ini");
                picker.FileTypeFilter.Add(".csv");
                picker.FileTypeFilter.Add(".yaml");
                picker.FileTypeFilter.Add(".yml");
                picker.FileTypeFilter.Add(".toml");
                picker.FileTypeFilter.Add(".xml");
                picker.FileTypeFilter.Add(".properties");
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var text = await FileIO.ReadTextAsync(file);
                    ViewModel.FileContent = text;
                    ViewModel.IsFileModified = true;
                    UpdateEditorMetrics();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogSafe("Import Error", ex.Message);
            }
        }

        private void ResetChanges_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ViewModel.FileContent = _originalFileContent;
                ViewModel.IsFileModified = false;
                UpdateEditorMetrics();
            }
            catch { }
        }

        private async void GoToLine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var inputBox = new TextBox { PlaceholderText = "Enter line number" };
                var dialog = new ContentDialog
                {
                    Title = "Go To Line",
                    Content = inputBox,
                    PrimaryButtonText = "Go",
                    CloseButtonText = "Cancel",
                    XamlRoot = XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && int.TryParse(inputBox.Text, out var line) &&
                    FindName("FileTextEditor") is TextBox editor)
                {
                    var text = editor.Text ?? string.Empty;
                    int currentLine = 1, pos = 0;
                    while (currentLine < line && pos < text.Length)
                    {
                        var next = text.IndexOf('\n', pos);
                        if (next < 0) break;
                        pos = next + 1;
                        currentLine++;
                    }
                    editor.SelectionStart = pos;
                    editor.Focus(FocusState.Programmatic);
                }
            }
            catch { }
        }

        private async void FindReplace_Click(object sender, RoutedEventArgs e)
        {
            var grid = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition() }, RowSpacing = 8 };
            var findBox = new TextBox { PlaceholderText = "Find..." };
            var replaceBox = new TextBox { PlaceholderText = "Replace with..." };
            Grid.SetRow(findBox, 0); Grid.SetRow(replaceBox, 1);
            grid.Children.Add(findBox); grid.Children.Add(replaceBox);

            var dialog = new ContentDialog
            {
                Title = "Find / Replace",
                Content = grid,
                PrimaryButtonText = "Replace Next",
                SecondaryButtonText = "Find Next",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (FindName("FileTextEditor") is not TextBox editor) return;
            var text = editor.Text ?? string.Empty;
            var toFind = findBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(toFind)) return;

            var start = editor.SelectionStart + editor.SelectionLength;
            var idx = text.IndexOf(toFind, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = text.IndexOf(toFind, 0, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                if (result == ContentDialogResult.Primary)
                {
                    var replace = replaceBox.Text ?? string.Empty;
                    editor.Text = text.Remove(idx, toFind.Length).Insert(idx, replace);
                    editor.SelectionStart = idx;
                    editor.SelectionLength = replace.Length;
                    ViewModel.FileContent = editor.Text;
                    ViewModel.IsFileModified = true;
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    editor.SelectionStart = idx;
                    editor.SelectionLength = toFind.Length;
                }
            }
        }

        private void FormatDocument_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = ViewModel.FileContent ?? string.Empty;
                if (ViewModel.IsJsonContext)
                {
                    using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });
                    var opts = new JsonWriterOptions { Indented = true, SkipValidation = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                    using var ms = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(ms, opts))
                    {
                        doc.WriteTo(writer);
                    }
                    ViewModel.FileContent = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
                else
                {
                    var tag = SnbtParser.Parse(text, false);
                    var formatted = (tag as NbtTag).ToSnbt(SnbtOptions.DefaultExpanded);
                    ViewModel.FileContent = formatted;
                }
                ViewModel.IsFileModified = true;
                UpdateEditorMetrics();
            }
            catch (Exception ex)
            {
                ViewModel.ValidationStatus = new ValidationStatusModel
                {
                    Message = $"Format failed: {ex.Message}",
                    Icon = "\uEA39",
                    BackgroundColor = "#FCE8E6",
                    ForegroundColor = "#B00020"
                };
            }
        }

        private void MinifyDocument_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = ViewModel.FileContent ?? string.Empty;
                if (ViewModel.IsJsonContext)
                {
                    using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });
                    var opts = new JsonWriterOptions { Indented = false, SkipValidation = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                    using var ms = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(ms, opts))
                    {
                        doc.WriteTo(writer);
                    }
                    ViewModel.FileContent = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
                else
                {
                    var tag = SnbtParser.Parse(text, false);
                    var compact = (tag as NbtTag).ToSnbt(SnbtOptions.Default);
                    ViewModel.FileContent = compact;
                }
                ViewModel.IsFileModified = true;
                UpdateEditorMetrics();
            }
            catch (Exception ex)
            {
                ViewModel.ValidationStatus = new ValidationStatusModel
                {
                    Message = $"Minify failed: {ex.Message}",
                    Icon = "\uEA39",
                    BackgroundColor = "#FCE8E6",
                    ForegroundColor = "#B00020"
                };
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

        private void EditorAreaGrid_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Sync initial state so no empty band appears when tools are hidden
                var isVisible = ViewModel?.IsSidePanelVisible ?? false;
                if (FindName("SidePanelColumn") is ColumnDefinition col)
                    col.Width = new GridLength(isVisible ? 280 : 0);
                if (FindName("SidePanelToggleIcon") is FontIcon icon)
                    icon.Glyph = isVisible ? "\uE76B" : "\uE76F"; // close vs tools icon
            }
            catch { }
        }
    }
}
