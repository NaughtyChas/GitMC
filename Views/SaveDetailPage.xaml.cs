using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
using Microsoft.UI.Xaml.Controls; // WebView2
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core; // CoreWebView2 events
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
        public bool ShowNormal => !IsSeparator && !IsCreateAction;
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
        private readonly IOperationManager _operationManager;
        private readonly ISessionLockMonitorService _sessionLockMonitorService;
        private string? _saveId;
        private Timer? _changeDetectionTimer;
        private Timer? _opProgressTimer;
        private string _originalFileContent = string.Empty;
        private List<string> _availableBranches = new();
        private bool _isBranchDropDownOpen;
        private BranchComboBoxItem? _lastValidBranchItem;
        private bool _isUpdatingBranchList;
        private bool _useWebEditor;
        // Large-file streaming state for WebView2
        private StreamReader? _editorStreamReader;
        private string? _editorStreamingPath;
        private bool _editorStreamingCompleted;
        private bool _webMessageHooked;
        private const int StreamingChunkChars = 200_000;
        // Virtualized paging (VS Code-like) for ultra-large files
        private bool _virtualizedMode;
        private readonly List<string> _virtualPages = new();
        private readonly List<int> _virtualPageLineCounts = new();
        private int _virtualLineBase; // number of lines logically before the first loaded page
        private const int VirtualPageLines = 2000; // lines per page
        private const int VirtualMaxPagesInMemory = 6; // keep a sliding window of pages

        public SaveDetailPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            // Use ServiceFactory to get shared service instances
            var services = ServiceFactory.Services;
            _nbtService = services.Nbt as NbtService ?? new NbtService();
            _configurationService = services.Configuration;
            _gitService = services.Git;
            _dataStorageService = services.DataStorage;
            _saveInitializationService = services.SaveInitialization;
            _sessionLockMonitorService = services.SessionLockMonitor;
            _operationManager = services.Operations;
            _minecraftAnalyzerService = ServiceFactory.MinecraftAnalyzer;
            _managedSaveService = new ManagedSaveService(_dataStorageService);
            _gitHubAppsService = ServiceFactory.GitHubApps;

            ViewModel = new SaveDetailViewModel();
            DataContext = this;

            // Observe ViewModel changes for dynamic UI tweaks
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Keep page instance alive across navigation to preserve progress UI
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            // Sync operation state on demand
            // (Periodic change detection will also sync during ticks)
        }

        public SaveDetailViewModel ViewModel { get; }

        // Expose translation progress properties for data binding
        public bool ShowTranslationInProgress => ViewModel.ShowTranslationInProgress;
        public double TranslationProgressValue => ViewModel.TranslationProgressValue;
        public string TranslationProgressMessage => ViewModel.TranslationProgressMessage;

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
                await LoadBranchesAsync();

                // Navigate to the default tab (Overview)
                NavigateToTab("Overview");

                // Start periodic change detection updates
                StartChangeDetectionUpdates();

                // Start session.lock monitoring for this save and subscribe to events
                if (ViewModel.SaveInfo?.Path is string sp)
                {
                    _sessionLockMonitorService.SessionEnded -= OnSessionEnded;
                    _sessionLockMonitorService.SessionInUseChanged -= OnSessionInUseChanged;
                    _sessionLockMonitorService.StartMonitoring(sp);
                    _sessionLockMonitorService.SessionEnded += OnSessionEnded;
                    _sessionLockMonitorService.SessionInUseChanged += OnSessionInUseChanged;

                    // Initialize InfoBar state based on current in-use status
                    if (!_sessionLockMonitorService.TryGetInUse(sp, out var inUse))
                    {
                        inUse = await IsSaveInUseAsync(sp);
                    }
                    UpdateSessionLockInfoBar(inUse);
                }

                // Sync operation state if there is an ongoing op for this save
                SyncOperationStatusWithManager();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InitializePageAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
        {
            try
            {
                var currentPath = ViewModel.SaveInfo?.Path;
                if (string.IsNullOrEmpty(currentPath) || !string.Equals(currentPath, e.SavePath, StringComparison.OrdinalIgnoreCase))
                    return;

                if (_autoCommitInProgress || ViewModel.IsCommitInProgress)
                    return;

                // Run on UI thread: refresh change detection and, if eligible, kick translation
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await UpdateChangeDetectionDataAsync();

                        if (ViewModel.SaveInfo?.AutoTranslateOnIdle == true)
                        {
                            var since = ViewModel.SaveInfo.LastSessionEndUtc;
                            // Update last session end to now for next cycle
                            ViewModel.SaveInfo.LastSessionEndUtc = DateTime.UtcNow;
                            await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);

                            // Immediately reflect translation-in-progress in UI and start polling on UI thread
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ViewModel.IsTranslationInProgress = true;
                                ViewModel.TranslationProgressMessage = "Starting translation...";
                                ViewModel.TranslationProgressValue = 0;

                                // Force property change notifications
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                            });

                            StartOperationProgressPolling();

                            var progress = new Progress<SaveInitStep>(step =>
                            {
                                // Update ViewModel properties for new UI binding
                                if (step.TotalProgress > 0)
                                {
                                    var value = Math.Min(100, Math.Max(0, (double)step.CurrentProgress / step.TotalProgress * 100.0));
                                    ViewModel.TranslationProgressValue = value;
                                    ViewModel.TranslationProgressMessage = step.Message ?? "Processing...";
                                }
                                else
                                {
                                    ViewModel.TranslationProgressMessage = step.Message ?? "Processing...";
                                }

                                // Update legacy UI elements for backwards compatibility
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

                            await _saveInitializationService.TranslateSinceAsync(currentPath!, since == default ? DateTimeOffset.UtcNow.AddMinutes(-30) : since, progress);
                            await LoadSaveDetailAsync();
                            await UpdateChangeDetectionDataAsync();
                            await RecomputeCanTranslateAsync();

                            // Translation finished; reset UI and stop polling on UI thread
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ViewModel.IsTranslationInProgress = false;
                                ViewModel.TranslationProgressMessage = string.Empty;
                                ViewModel.TranslationProgressValue = 0;

                                // Force property change notifications
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                            });

                            StopOperationProgressPolling();
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void SyncOperationStatusWithManager()
        {
            try
            {
                var path = ViewModel.SaveInfo?.Path;
                if (string.IsNullOrEmpty(path)) return;
                var active = _operationManager.GetActive(path);
                var running = active != null;
                if (ViewModel.IsCommitInProgress != running)
                {
                    ViewModel.IsCommitInProgress = running;
                    _ = RecomputeCanTranslateAsync();
                }

                // Mirror translation progress UI when active translate op exists
                if (active != null && active.Type == OperationType.Translate)
                {
                    var pct = active.TotalSteps > 0 ? Math.Max(0, Math.Min(100, (double)active.CurrentStep / active.TotalSteps * 100.0)) : 0;

                    // Update ViewModel translation state on UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        System.Diagnostics.Debug.WriteLine($"SyncOperationStatusWithManager: Setting IsTranslationInProgress = true, Progress = {pct}%, Message = {active.Message}");
                        ViewModel.IsTranslationInProgress = true;
                        ViewModel.TranslationProgressValue = pct;
                        ViewModel.TranslationProgressMessage = active.Message ?? "Processing...";

                        // Force property change notifications
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));

                        // Update existing UI elements for backward compatibility
                        if (FindName("TranslationProgressBar") is ProgressBar p1) p1.Value = pct;
                        if (FindName("TranslationProgressText") is TextBlock t1) t1.Text = $"{pct:F0}%";
                        if (FindName("TranslationStepText") is TextBlock s1) s1.Text = active.Message;

                        if (FindName("TranslateRequiredProgressBar") is ProgressBar p2) p2.Value = pct;
                        if (FindName("TranslateRequiredProgressText") is TextBlock t2) t2.Text = $"{pct:F0}%";
                        if (FindName("TranslateRequiredStepText") is TextBlock s2) s2.Text = active.Message;
                    });

                    // Ensure a lightweight polling timer runs while translation is active
                    StartOperationProgressPolling();
                }
                else
                {
                    // Only reset translation state if we're not in a manual translation operation
                    // _autoCommitInProgress indicates manual translation is in progress
                    if (!_autoCommitInProgress)
                    {
                        // Translation completed or not running - update on UI thread
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            System.Diagnostics.Debug.WriteLine("SyncOperationStatusWithManager: Setting IsTranslationInProgress = false");
                            ViewModel.IsTranslationInProgress = false;
                            ViewModel.TranslationProgressValue = 0.0;
                            ViewModel.TranslationProgressMessage = "";

                            // Force property change notifications
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                        });

                        StopOperationProgressPolling();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("SyncOperationStatusWithManager: Manual translation in progress, not resetting translation state");
                    }
                }
            }
            catch { }
        }


        private void StartOperationProgressPolling()
        {
            if (_opProgressTimer != null) return;
            _opProgressTimer = new Timer(_ =>
            {
                try { DispatcherQueue.TryEnqueue(() => SyncOperationStatusWithManager()); } catch { }
            }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        private void StopOperationProgressPolling()
        {
            _opProgressTimer?.Dispose();
            _opProgressTimer = null;
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
                if (ViewModel.SaveInfo?.IsGitInitialized == true && !string.IsNullOrEmpty(ViewModel.SaveInfo.Path))
                {
                    var commits = await _gitService.GetCommitHistoryAsync(5, ViewModel.SaveInfo.Path);
                    var commitInfos = commits.Select(commit => new CommitInfo
                    {
                        Sha = commit.Sha,
                        Message = commit.Message,
                        Author = commit.AuthorName,
                        Timestamp = commit.AuthorDate
                    }).ToList();

                    ViewModel.RecentCommits.Clear();
                    foreach (var commit in commitInfos) ViewModel.RecentCommits.Add(commit);
                    UpdateRecentActivityUI();
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
                UpdateRecentActivityUI();
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
                if (FindName("TranslatePrimaryButton") is Button tacButton)
                {
                    tacButton.Visibility = ViewModel.SaveInfo.CurrentStatus == ManagedSaveInfo.SaveStatus.Modified
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    tacButton.IsEnabled = ViewModel.CanTranslate;
                    UpdateTranslateButtonStyle();
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
                var branches = _availableBranches ?? new List<string>();

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

        private List<string> GetAvailableBranches(ManagedSaveInfo saveInfo) => _availableBranches ?? new List<string>();

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
                _autoCommitInProgress = true;

                // Ensure UI updates happen on UI thread immediately
                DispatcherQueue.TryEnqueue(() =>
                {
                    // Immediately switch UI to translation-in-progress and start polling
                    ViewModel.IsTranslationInProgress = true;
                    ViewModel.TranslationProgressMessage = "Starting translation...";
                    ViewModel.TranslationProgressValue = 0;

                    // Force immediate property change notification
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));

                    Debug.WriteLine("TranslateButton: UI state set to translation in progress");
                });

                StartOperationProgressPolling();

                // Wire up translation progress to the Translation Status UI (legacy support)
                if (FindName("TranslationStepText") is TextBlock stepText &&
                    FindName("TranslationProgressBar") is ProgressBar pb &&
                    FindName("TranslationProgressText") is TextBlock pct)
                {
                    stepText.Text = "Starting translation...";
                    pb.Value = 0;
                    pct.Text = "0%";
                }

                // Add a minimum delay to ensure UI is visible
                await Task.Delay(500);

                var progress = new Progress<SaveInitStep>(step =>
                {
                    Debug.WriteLine($"TranslateButton Progress: {step.Message}");

                    // Ensure all UI updates happen on UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // Update ViewModel properties for new UI binding
                        if (step.TotalProgress > 0)
                        {
                            var value = Math.Min(100, Math.Max(0, (double)step.CurrentProgress / step.TotalProgress * 100.0));
                            ViewModel.TranslationProgressValue = value;
                            ViewModel.TranslationProgressMessage = step.Message ?? "Processing...";
                            Debug.WriteLine($"TranslateButton: Progress updated to {value}% - {step.Message}");
                        }
                        else
                        {
                            ViewModel.TranslationProgressMessage = step.Message ?? "Processing...";
                            Debug.WriteLine($"TranslateButton: Message updated to {step.Message}");
                        }

                        // Force property change notifications
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));

                        // Update legacy UI elements for backwards compatibility
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
                });

                Debug.WriteLine("TranslateButton: Starting TranslateChangedAsync...");
                var ok = await _saveInitializationService.TranslateChangedAsync(ViewModel.SaveInfo.Path, progress);
                Debug.WriteLine($"TranslateButton: TranslateChangedAsync completed with result: {ok}");

                if (ok)
                {
                    // Set completion status on UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.TranslationProgressMessage = "Translation complete";
                        ViewModel.TranslationProgressValue = 100;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));

                        if (FindName("TranslationStepText") is TextBlock s) s.Text = "Translation complete";
                        Debug.WriteLine("TranslateButton: Set completion status");
                    });

                    await LoadSaveDetailAsync();
                    await UpdateChangeDetectionDataAsync();
                    await RecomputeCanTranslateAsync();

                    // Allow user to see completion status for a moment
                    Debug.WriteLine("TranslateButton: Waiting 1.5s before cleanup...");
                    await Task.Delay(1500);
                }
                else
                {
                    Debug.WriteLine("TranslateButton: Translation returned false, waiting 2s before cleanup...");
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranslateButton_Click error: {ex.Message}");
                // Set error status on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.TranslationProgressMessage = "Translation failed";
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                });
                await Task.Delay(2000);
            }
            finally
            {
                _autoCommitInProgress = false;
                UpdateStatusInfoBar();
                await RecomputeCanTranslateAsync();

                Debug.WriteLine("TranslateButton: Resetting translation UI state...");
                // Reset translation-in-progress UI and stop polling on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.IsTranslationInProgress = false;
                    ViewModel.TranslationProgressMessage = string.Empty;
                    ViewModel.TranslationProgressValue = 0;

                    // Force property change notifications
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                    Debug.WriteLine("TranslateButton: Translation UI state reset to normal");
                });

                StopOperationProgressPolling();
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

        private async void BranchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isUpdatingBranchList)
                    return;
                if (sender is ComboBox comboBox && comboBox.SelectedItem is BranchComboBoxItem selectedItem)
                {
                    // Disallow selecting the separator visually by restoring last valid selection
                    if (selectedItem.IsSeparator)
                    {
                        if (_lastValidBranchItem != null)
                        {
                            try
                            {
                                _isUpdatingBranchList = true;
                                comboBox.SelectedItem = _lastValidBranchItem;
                            }
                            finally
                            {
                                _isUpdatingBranchList = false;
                            }
                        }
                        return;
                    }

                    if (selectedItem.IsCreateAction)
                    {
                        await CreateBranchInteractiveAsync();
                        // After creating, reload list and restore selection to the current branch
                        await LoadBranchesAsync();
                        if (comboBox.SelectedItem is BranchComboBoxItem cur && !cur.IsSeparator && !cur.IsCreateAction)
                        {
                            _lastValidBranchItem = cur;
                        }
                    }
                    else if (!selectedItem.IsSeparator && !string.IsNullOrEmpty(selectedItem.BranchName))
                    {
                        var branchName = selectedItem.BranchName;
                        Debug.WriteLine($"Switching to branch: {branchName}");
                        if (ViewModel.SaveInfo?.Path is string repoPath)
                        {
                            // If already on this branch, no-op to avoid redundant operations
                            var currentBranch = ViewModel.SaveInfo.Branch ?? string.Empty;
                            if (string.Equals(currentBranch, branchName, StringComparison.Ordinal))
                            {
                                if (comboBox.SelectedItem is BranchComboBoxItem cur && !cur.IsSeparator && !cur.IsCreateAction)
                                {
                                    _lastValidBranchItem = cur;
                                }
                                return;
                            }
                            var result = await _gitService.CheckoutBranchAsync(branchName, repoPath);
                            if (result.Success)
                            {
                                var status = await _gitService.GetStatusAsync(repoPath);
                                ViewModel.SaveInfo.Branch = string.IsNullOrWhiteSpace(status.CurrentBranch) ? branchName : status.CurrentBranch;
                                await LoadBranchesAsync();
                                RefreshStatusDisplays();
                                await LoadChangedFilesDataAsync();
                                await LoadRecentCommitsAsync();
                                // Track last valid selection
                                if (comboBox.SelectedItem is BranchComboBoxItem cur && !cur.IsSeparator && !cur.IsCreateAction)
                                {
                                    _lastValidBranchItem = cur;
                                }
                            }
                            else
                            {
                                await ShowErrorDialogSafe("Checkout Failed", result.ErrorMessage);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error switching branch: {ex.Message}");
                await ShowErrorDialogSafe("Error", $"Failed to switch branch: {ex.Message}");
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

        private async Task LoadBranchesAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path) &&
                    FindName("BranchComboBox") is ComboBox branchComboBox)
                {
                    // Don't refresh items while the dropdown is open; this causes it to retract immediately
                    if (_isBranchDropDownOpen)
                        return;

                    var repoPath = ViewModel.SaveInfo.Path;
                    var rawBranches = await _gitService.GetBranchesAsync(repoPath);
                    var parsed = rawBranches
                        .Select(b => new { Raw = b, IsCurrent = b.StartsWith("* "), Name = b.TrimStart('*', ' ') })
                        .ToList();
                    _availableBranches = parsed.Select(p => p.Name).Distinct().ToList();

                    var comboBoxItems = new List<BranchComboBoxItem>();

                    foreach (var b in parsed)
                    {
                        comboBoxItems.Add(new BranchComboBoxItem
                        {
                            DisplayName = b.Name,
                            BranchName = b.Name,
                            IsSeparator = false,
                            IsCreateAction = false
                        });
                    }

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

                    try
                    {
                        _isUpdatingBranchList = true;
                        branchComboBox.ItemsSource = comboBoxItems;

                        var currentName = parsed.FirstOrDefault(p => p.IsCurrent)?.Name
                                          ?? ViewModel.SaveInfo.Branch
                                          ?? ViewModel.SaveInfo.GitHubDefaultBranch
                                          ?? "main";
                        var currentBranchItem = comboBoxItems.FirstOrDefault(item => item.BranchName == currentName)
                                                ?? comboBoxItems.FirstOrDefault(item => item.BranchName == "main");
                        branchComboBox.SelectedItem = currentBranchItem;
                        _lastValidBranchItem = currentBranchItem;
                        UpdateBranchTagsDisplay();
                    }
                    finally
                    {
                        _isUpdatingBranchList = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading branches: {ex.Message}");
            }
        }

        private void BranchComboBox_DropDownOpened(object sender, object e)
        {
            _isBranchDropDownOpen = true;
        }

        private void BranchComboBox_DropDownClosed(object sender, object e)
        {
            _isBranchDropDownOpen = false;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SaveDetailViewModel.CanTranslate))
            {
                UpdateTranslateButtonStyle();
            }
            else if (e.PropertyName == nameof(SaveDetailViewModel.ShowTranslationInProgress) ||
                     e.PropertyName == nameof(SaveDetailViewModel.IsTranslationInProgress))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
            }
            else if (e.PropertyName == nameof(SaveDetailViewModel.TranslationProgressValue))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
            }
            else if (e.PropertyName == nameof(SaveDetailViewModel.TranslationProgressMessage))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
            }
        }

        private void UpdateTranslateButtonStyle()
        {
            if (FindName("TranslatePrimaryButton") is Button btn)
            {
                // Semantics:
                // - AccentButtonStyle: GitMC is out of date (needs translation)
                // - Default style: GitMC is consistent with save (no translation needed)
                btn.Style = ViewModel.CanTranslate
                    ? Application.Current.Resources["AccentButtonStyle"] as Style
                    : null; // null = default style
                btn.IsEnabled = true; // Always clickable, style indicates status
            }
        }

        private async Task CreateBranchInteractiveAsync()
        {
            try
            {
                var inputBox = new TextBox { PlaceholderText = "new-branch-name" };
                var dialog = new ContentDialog
                {
                    Title = "Create new branch",
                    PrimaryButtonText = "Create",
                    CloseButtonText = "Cancel",
                    Content = inputBox,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
                {
                    var name = inputBox.Text.Trim();
                    var repoPath = ViewModel.SaveInfo?.Path;
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var create = await _gitService.CreateBranchAsync(name, repoPath);
                        if (!create.Success)
                        {
                            await ShowErrorDialogSafe("Create Branch Failed", create.ErrorMessage);
                            return;
                        }

                        var checkout = await _gitService.CheckoutBranchAsync(name, repoPath);
                        if (!checkout.Success)
                        {
                            await ShowErrorDialogSafe("Checkout Failed", checkout.ErrorMessage);
                            return;
                        }

                        var status = await _gitService.GetStatusAsync(repoPath);
                        ViewModel.SaveInfo!.Branch = status.CurrentBranch;

                        await LoadBranchesAsync();
                        RefreshStatusDisplays();
                        await LoadRecentCommitsAsync();
                        await LoadChangedFilesDataAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogSafe("Error", $"Failed to create branch: {ex.Message}");
            }
        }

        private void UpdateRecentActivityUI()
        {
            try
            {
                var container = FindName("RecentActivityContainer") as StackPanel;
                if (container == null) return;

                container.Children.Clear();

                var commits = ViewModel.RecentCommits?.ToList() ?? new List<CommitInfo>();
                if (commits.Count == 0)
                {
                    container.Children.Add(new TextBlock
                    {
                        Text = "No recent activity",
                        Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        FontSize = 12
                    });
                    return;
                }

                foreach (var c in commits)
                {
                    container.Children.Add(CreateCommitActivityItem(c));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating Recent Activity UI: {ex.Message}");
            }
        }

        private Border CreateCommitActivityItem(CommitInfo commit)
        {
            var card = new Border
            {
                Padding = new Thickness(12),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Spacing = 4 };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new FontIcon
            {
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                Glyph = "\uE73E"
            });
            titleRow.Children.Add(new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Text = string.IsNullOrWhiteSpace(commit.Message) ? "(no commit message)" : commit.Message,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            left.Children.Add(titleRow);

            var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var age = DateTime.Now - commit.Timestamp;
            var ageText = age.TotalDays >= 1 ? $"{(int)age.TotalDays} day(s) ago" :
                          age.TotalHours >= 1 ? $"{(int)age.TotalHours} hour(s) ago" :
                          age.TotalMinutes >= 1 ? $"{(int)age.TotalMinutes} min ago" : "just now";
            metaRow.Children.Add(new TextBlock
            {
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Text = $"by {commit.Author} • {ageText}"
            });
            metaRow.Children.Add(new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"], Text = "•" });
            metaRow.Children.Add(new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Text = commit.Sha.Length > 7 ? commit.Sha[..7] : commit.Sha
            });
            left.Children.Add(metaRow);

            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var pill = new Border
            {
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(12)
            };
            var pillText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Text = "commit"
            };
            // Prefer theme resource brush if available; fallback ignored
            if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var brushObj) && brushObj is Brush onAccent)
            {
                pillText.Foreground = onAccent;
            }
            pill.Child = pillText;
            Grid.SetColumn(pill, 1);
            grid.Children.Add(pill);

            card.Child = grid;
            return card;
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
                // 1.17+ semantics: session.lock exists and is exclusively locked while the world is open
                var sessionLock = System.IO.Path.Combine(savePath, "session.lock");
                if (!File.Exists(sessionLock)) return false;
                if (IsFileLocked(sessionLock)) return true;
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
                ViewModel.IsChangesLoading = true;
                // Get region-level real changes (filtering out timestamp-only diffs)
                var changedChunks = await _saveInitializationService.DetectRealChangedChunksAsync(ViewModel.SaveInfo.Path);
                var gitStatus = await _gitService.GetStatusAsync(ViewModel.SaveInfo.Path);

                // Group and categorize files
                var fileGroups = new Dictionary<FileCategory, ChangedFileGroup>();

                // Process .mca files by source folder (region, entities, poi)
                var mcaFiles = gitStatus.ModifiedFiles
                    .Where(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var regionFile in mcaFiles)
                {
                    var source = GetMcaSourceFolder(regionFile);
                    (FileCategory cat, string name, string icon) groupMeta = source switch
                    {
                        "entities" => (FileCategory.Entity, "Entity Region Files", "\uE77B"),
                        "poi" => (FileCategory.Data, "POI Region Files", "\uE8A5"),
                        _ => (FileCategory.Region, "Region Files", "\uE8B7")
                    };

                    var group = GetOrCreateGroup(fileGroups, groupMeta.cat, groupMeta.name, groupMeta.icon);

                    var fullPath = Path.Combine(ViewModel.SaveInfo.Path, regionFile);
                    var fileInfo = new FileInfo(fullPath);

                    var changedFile = new ChangedFile
                    {
                        FileName = Path.GetFileName(regionFile),
                        RelativePath = regionFile,
                        FullPath = fullPath,
                        Status = ChangeStatus.Modified,
                        Category = groupMeta.cat,
                        FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                        LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                        DisplaySize = FormatFileSize(fileInfo.Exists ? fileInfo.Length : 0),
                        StatusText = "Modified",
                        CategoryText = groupMeta.name,
                        IconGlyph = groupMeta.icon,
                        StatusColor = "#FF9800",
                        ChunkCount = await CountRegionChunkSnbtAsync(ViewModel.SaveInfo.Path, regionFile)
                    };

                    TryPopulateTranslationInfo(changedFile);
                    group.Files.Add(changedFile);
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
                ViewModel.HasChangedFiles = ViewModel.ChangedFileGroups.Any() && ViewModel.ChangedFileGroups.SelectMany(g => g.Files).Any();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading changed files: {ex.Message}");
            }
            finally
            {
                ViewModel.IsChangesLoading = false;
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

            // Region-like files: split by source folder
            if (fileName.EndsWith(".mca") || fileName.EndsWith(".mcc"))
            {
                var src = GetMcaSourceFolder(filePath);
                return src switch
                {
                    "entities" => (FileCategory.Entity, "Entity Region Files", "\uE77B"),
                    "poi" => (FileCategory.Data, "POI Region Files", "\uE8A5"),
                    _ => (FileCategory.Region, "Region Files", "\uE8B7")
                };
            }

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
                // Clear selections from other groups so a file can be re-selected later
                if (FindName("ChangedFilesScrollViewer") is ScrollViewer sv && sv.Content is DependencyObject contentRoot)
                {
                    var groups = FindDescendant<ItemsControl>(contentRoot);
                    if (groups != null)
                    {
                        foreach (var container in groups.Items.Select(item => groups.ContainerFromItem(item)).OfType<FrameworkElement>())
                        {
                            var list = FindDescendant<ListView>(container);
                            if (list != null && !ReferenceEquals(list, listView))
                            {
                                list.SelectedItem = null;
                            }
                        }
                    }
                }
                ViewModel.SelectedChangedFile = selectedFile;
                _ = LoadSelectedFileEditorAsync();
            }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
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
                // If editor has unsaved changes, save them first
                if (ViewModel.IsFileModified && ViewModel.SelectedChangedFile?.EditorPath is string editorPath1)
                {
                    var abs1 = Path.IsPathRooted(editorPath1) ? editorPath1 : Path.Combine(ViewModel.SaveInfo.Path, editorPath1);
                    await File.WriteAllTextAsync(abs1, ViewModel.FileContent ?? string.Empty);
                    ViewModel.IsFileModified = false;
                }

                var progress = new Progress<SaveInitStep>(step =>
                {
                    ViewModel.CommitProgressMessage = step.Message;
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
                ViewModel.CommitProgressMessage = string.Empty;
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
                // If editor has unsaved changes, save them first
                if (ViewModel.IsFileModified && ViewModel.SelectedChangedFile?.EditorPath is string editorPath2)
                {
                    var abs2 = Path.IsPathRooted(editorPath2) ? editorPath2 : Path.Combine(ViewModel.SaveInfo.Path, editorPath2);
                    await File.WriteAllTextAsync(abs2, ViewModel.FileContent ?? string.Empty);
                    ViewModel.IsFileModified = false;
                }

                var progress = new Progress<SaveInitStep>(step =>
                {
                    ViewModel.CommitProgressMessage = step.Message;
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
                    ViewModel.CommitMessage = string.Empty;
                    ViewModel.CommitDescription = string.Empty;

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
                ViewModel.CommitProgressMessage = string.Empty;
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
            var form = FindName("CommitFormContent") as FrameworkElement;
            var actions = FindName("CommitActionsContent") as FrameworkElement;
            var rotate = FindName("CommitSectionCollapseIconTransform") as RotateTransform;
            if (form == null || actions == null || rotate == null) return;

            var isCollapsed = form.Visibility == Visibility.Collapsed;

            if (isCollapsed)
            {
                // Expand the commit section
                form.Visibility = Visibility.Visible;
                actions.Visibility = Visibility.Visible;

                // Rotate the icon to point up (expanded state)
                rotate.Angle = 180;
            }
            else
            {
                // Collapse the commit section
                form.Visibility = Visibility.Collapsed;
                actions.Visibility = Visibility.Collapsed;

                // Rotate the icon to point down (collapsed state)
                rotate.Angle = 0;
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

        private static string GetMcaSourceFolder(string mcaRelativePath)
        {
            // Extract top-level source folder for .mca path like "region/r.x.z.mca" or "entities/r.x.z.mca" or "poi/r.x.z.mca"
            var parts = mcaRelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].ToLowerInvariant() : "region";
        }

        private string GetRegionChunksFolder(string mcaRelativePath)
        {
            // GitMC/<source>/<r.x.z.mca>/
            var source = GetMcaSourceFolder(mcaRelativePath);
            var fileName = Path.GetFileNameWithoutExtension(mcaRelativePath);
            var gitMc = GetGitMcFolder();
            return Path.Combine(gitMc, source, fileName + ".mca");
        }

        private string? GetAnyChunkSnbtForMca(string mcaRelativePath)
        {
            try
            {
                var dir = GetRegionChunksFolder(mcaRelativePath);
                if (Directory.Exists(dir))
                    return Directory.EnumerateFiles(dir, "chunk_*_*.snbt").FirstOrDefault();
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
                    // For region-like files, look for any chunk SNBT under GitMC/<source>/r.x.z.mca
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
                    // For region files (.mca), prefer showing the region map first instead of auto-opening an arbitrary chunk
                    if (ext == ".mca")
                    {
                        file.EditorPath = null; // map-first UX
                    }
                    else
                    {
                        file.EditorPath = file.SnbtPath;
                    }
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
                var info = ViewModel.SaveInfo;
                var can = false;
                if (info?.Path != null
                    && info.CurrentStatus == ManagedSaveInfo.SaveStatus.Modified
                    && !ViewModel.IsCommitInProgress
                    && !_autoCommitInProgress)
                {
                    var inUse = await IsSaveInUseAsync(info.Path);
                    if (!inUse)
                    {
                        // Use service-level detector that considers LastUpdate-only filtering and non-region files
                        can = await _saveInitializationService.HasPendingTranslationsAsync(info.Path);
                    }
                }
                ViewModel.CanTranslate = can;
                UpdateTranslateButtonStyle();
            }
            catch { ViewModel.CanTranslate = false; }
        }

        private async Task LoadSelectedFileEditorAsync()
        {
            try
            {
                ViewModel.ValidationStatus = null;
                var editorPath = ViewModel.SelectedChangedFile?.EditorPath;
                // Reset Region Map UI by default
                if (FindName("RegionMapOverlay") is FrameworkElement _rmOverlayInit) _rmOverlayInit.Visibility = Visibility.Collapsed;
                if (FindName("RegionMapButton") is FrameworkElement _rmButtonInit) _rmButtonInit.Visibility = Visibility.Collapsed;

                // If MCA selected and no specific editor path yet, show region map if translated; else show hint
                if (ViewModel.SelectedChangedFile is { } sel && string.IsNullOrEmpty(editorPath))
                {
                    var ext = Path.GetExtension(sel.FullPath).ToLowerInvariant();
                    if (ext == ".mca")
                    {
                        if (sel.IsTranslated)
                        {
                            await ShowRegionMapAsync();
                            return;
                        }
                        else
                        {
                            _originalFileContent = string.Empty;
                            ViewModel.FileContent = "This region file is not translated yet. Click 'Translate' in the InfoBar or 'Force Translation' in Tools to generate SNBT for viewing.";
                            ViewModel.IsFileModified = false;
                            UpdateEditorMetrics();
                            return;
                        }
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

                    // Heuristic: if file > 1.5 MB or > 120k chars, prefer web-based code editor (with streaming) to reduce UI thread pressure
                    var info = new FileInfo(pathToLoad);
                    // Always use Monaco; choose between normal streaming vs. virtualized window for ultra-large files
                    var ultraLarge = info.Exists && info.Length > 5_000_000; // ~5MB threshold for virtualization by lines
                    string text;
                    if (!ultraLarge)
                    {
                        text = await File.ReadAllTextAsync(pathToLoad);
                        _originalFileContent = text;
                        ViewModel.FileContent = text;
                        ViewModel.IsFileModified = false;
                        UpdateEditorMetrics();

                        _useWebEditor = true;
                        await EnsureEditorSurfaceAsync();
                        await RenderEditorContentAsync();
                        UpdateRegionMapButtonVisibility();
                    }
                    else
                    {
                        // Ultra-large: enable virtualized paging in Monaco (VS Code-like window)
                        _useWebEditor = true;
                        _virtualizedMode = true;
                        await InitializeVirtualizedWindowAsync(pathToLoad, info);
                        UpdateRegionMapButtonVisibility();
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

        private void UpdateRegionMapButtonVisibility()
        {
            try
            {
                if (FindName("RegionMapButton") is FrameworkElement mapBtn)
                {
                    var isRegion = ViewModel.SelectedChangedFile?.FullPath.EndsWith(".mca", StringComparison.OrdinalIgnoreCase) == true;
                    var translated = ViewModel.SelectedChangedFile?.IsTranslated == true;
                    mapBtn.Visibility = (!ViewModel.IsRegionMapVisible && isRegion && translated) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { /* ignore */ }
        }

        private void ToggleRegionMap(bool show)
        {
            if (FindName("RegionMapOverlay") is FrameworkElement overlay)
                overlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ViewModel.IsRegionMapVisible = show;
            UpdateRegionMapButtonVisibility();
        }

        private async Task ShowRegionMapAsync()
        {
            try
            {
                ViewModel.IsRegionMapLoading = true;
                var sel = ViewModel.SelectedChangedFile;
                if (sel == null) { ToggleRegionMap(false); return; }
                var ext = Path.GetExtension(sel.FullPath).ToLowerInvariant();
                if (ext != ".mca") { ToggleRegionMap(false); return; }

                // chunks directory under GitMC: <source>/r.x.z.mca/
                var fileName = Path.GetFileNameWithoutExtension(sel.FullPath);
                var regionDir = GetRegionChunksFolder(sel.RelativePath);
                var items = new List<GitMC.Models.ChunkInfo>();
                if (Directory.Exists(regionDir))
                {
                    foreach (var file in Directory.EnumerateFiles(regionDir, "chunk_*_*.snbt"))
                    {
                        var name = Path.GetFileName(file);
                        var m = System.Text.RegularExpressions.Regex.Match(name, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var cx) && int.TryParse(m.Groups[2].Value, out var cz))
                        {
                            items.Add(new GitMC.Models.ChunkInfo
                            {
                                ChunkX = cx,
                                ChunkZ = cz,
                                IsModified = false,
                                IsNew = false,
                                IsDeleted = false,
                                SizeBytes = new FileInfo(file).Length
                            });
                        }
                    }
                }

                // Sort rows by Z then X for stable presentation
                items.Sort((a, b) =>
                {
                    var z = a.ChunkZ.CompareTo(b.ChunkZ);
                    return z != 0 ? z : a.ChunkX.CompareTo(b.ChunkX);
                });

                if (FindName("RegionChunkGrid") is ItemsControl grid)
                {
                    grid.ItemsSource = items;
                }

                // Show map only when we actually have chunk files; otherwise fall back to Translation Required if not translated
                ViewModel.IsRegionMapEmpty = items.Count == 0;
                ToggleRegionMap(true);
                await Task.CompletedTask;
            }
            catch
            {
                ToggleRegionMap(false);
            }
            finally
            {
                ViewModel.IsRegionMapLoading = false;
            }
        }

        private async void RegionMapButton_Click(object sender, RoutedEventArgs e)
        {
            await ShowRegionMapAsync();
        }

        private async void RegionChunkGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e?.ClickedItem is GitMC.Models.ChunkInfo chunk)
            {
                await LoadChunkSnbtAsync(chunk);
            }
        }

        private async void ChunkCell_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is GitMC.Models.ChunkInfo chunk)
                {
                    await LoadChunkSnbtAsync(chunk);
                }
            }
            catch { /* ignore */ }
        }

        private async Task LoadChunkSnbtAsync(GitMC.Models.ChunkInfo chunk)
        {
            if (ViewModel.SelectedChangedFile is not { } sel) return;
            var fileName = Path.GetFileNameWithoutExtension(sel.FullPath);
            var parts = fileName.Split('.');
            if (parts.Length >= 3 && parts[0] == "r" && int.TryParse(parts[1], out var rx) && int.TryParse(parts[2], out var rz))
            {
                var dir = GetRegionChunksFolder(sel.RelativePath);
                var chunkPath = Path.Combine(dir, $"chunk_{chunk.ChunkX}_{chunk.ChunkZ}.snbt");
                if (File.Exists(chunkPath))
                {
                    sel.EditorPath = chunkPath;
                    ToggleRegionMap(false);
                    await LoadSelectedFileEditorAsync();
                }
            }
        }

        // XAML helper: when to show the Translation Required card
        public Visibility GetTranslationRequiredVisibility(Models.ChangedFile? file)
        {
            if (file == null) return Visibility.Collapsed;
            // show only if not translated AND not directly editable AND no editor path resolved
            var show = !file.IsTranslated && !file.IsDirectEditable && string.IsNullOrEmpty(file.EditorPath);
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_useWebEditor) return; // Web editor changes flow via JS -> host bridge
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

                // Show translation overlay while we perform the operation on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.IsTranslationInProgress = true;
                    ViewModel.TranslationProgressMessage = "Translating...";
                    ViewModel.TranslationProgressValue = 0;

                    // Force immediate property change notifications
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                });

                StartOperationProgressPolling();

                if (ext == ".mca")
                {
                    // Trigger translation for changed chunks in the save; simplest and consistent
                    var stepProgress = new Progress<SaveInitStep>(s =>
                    {
                        Debug.WriteLine($"Translate: {s.Message}");
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ViewModel.TranslationProgressMessage = s.Message ?? "Processing...";
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                        });
                    });
                    await _saveInitializationService.TranslateChangedAsync(ViewModel.SaveInfo.Path, stepProgress);
                }
                else
                {
                    var snbtPath = GetSnbtPathForRelative(file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(snbtPath)!);
                    var progress = new Progress<string>(msg =>
                    {
                        Debug.WriteLine($"Translation: {msg}");
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ViewModel.TranslationProgressMessage = msg;
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                        });
                    });
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
            finally
            {
                // Reset overlay and stop polling on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    ViewModel.IsTranslationInProgress = false;
                    ViewModel.TranslationProgressMessage = string.Empty;
                    ViewModel.TranslationProgressValue = 0;

                    // Force property change notifications
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTranslationInProgress)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressMessage)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationProgressValue)));
                });

                StopOperationProgressPolling();
            }
        }

        private void WordWrapToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_useWebEditor)
            {
                if (FindName("FileTextEditor") is TextBox editor)
                    editor.TextWrapping = ViewModel.IsWordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
            }
            else
            {
                _ = InvokeWebEditor("setWordWrap", ViewModel.IsWordWrapEnabled ? "true" : "false");
            }
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is string family)
            {
                if (!string.Equals(ViewModel.SelectedFontFamily, family, StringComparison.Ordinal))
                {
                    ViewModel.SelectedFontFamily = family;
                    if (_useWebEditor)
                        _ = InvokeWebEditor("setFontFamily", family);
                    else if (FindName("FileTextEditor") is TextBox editor)
                        editor.FontFamily = new FontFamily(family);
                }
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_useWebEditor)
                _ = InvokeWebEditor("setFontSize", ViewModel.EditorFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else if (FindName("FileTextEditor") is TextBox editor)
                editor.FontSize = ViewModel.EditorFontSize;
        }

        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is double d)
            {
                if (Math.Abs(ViewModel.EditorFontSize - d) > double.Epsilon)
                {
                    ViewModel.EditorFontSize = d;
                    if (_useWebEditor)
                        _ = InvokeWebEditor("setFontSize", d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else if (FindName("FileTextEditor") is TextBox editor)
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
                if (_useWebEditor)
                    _ = RenderEditorContentAsync();
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
            if (_useWebEditor)
            {
                // Open simple prompt then delegate to web editor replace-next
                var webGrid = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition() }, RowSpacing = 8 };
                var webFindBox = new TextBox { PlaceholderText = "Find..." };
                var webReplaceBox = new TextBox { PlaceholderText = "Replace with..." };
                Grid.SetRow(webFindBox, 0); Grid.SetRow(webReplaceBox, 1);
                webGrid.Children.Add(webFindBox); webGrid.Children.Add(webReplaceBox);
                var webDialog = new ContentDialog { Title = "Find / Replace", Content = webGrid, PrimaryButtonText = "Replace Next", SecondaryButtonText = "Find Next", CloseButtonText = "Close", XamlRoot = XamlRoot };
                var webResult = await webDialog.ShowAsync();
                if (webResult == ContentDialogResult.Primary)
                    await InvokeWebEditor("replaceNext", System.Text.Json.JsonSerializer.Serialize(new { find = webFindBox.Text, replace = webReplaceBox.Text }));
                else if (webResult == ContentDialogResult.Secondary)
                    await InvokeWebEditor("findNext", webFindBox.Text ?? "");
                return;
            }
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
            if (_useWebEditor)
            {
                _ = InvokeWebEditor("minify", "");
                return;
            }
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

        #region WebView2 Monaco Editor (large file optimization)
        private async Task EnsureEditorSurfaceAsync()
        {
            if (_useWebEditor)
            {
                if (FindName("CodeWebView") is WebView2 web && FindName("FileTextEditorContainer") is FrameworkElement box)
                {
                    box.Visibility = Visibility.Collapsed;
                    web.Visibility = Visibility.Visible;
                    if (web.Source == null)
                    {
                        // Load an embedded minimal Monaco host; prefer static assets if present
                        await web.EnsureCoreWebView2Async();
                        // Map app:// to local app folder; expect Monaco at app://monaco/vs/*
                        var baseDir = AppContext.BaseDirectory;
                        web.CoreWebView2.SetVirtualHostNameToFolderMapping("app", baseDir, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                        var useStatic = System.IO.Directory.Exists(System.IO.Path.Combine(baseDir, "monaco", "vs"));
                        var html = GetMonacoHostHtml(useStatic);
                        HookWebMessageBridge(web);
                        web.NavigateToString(html);
                    }
                    else if (!_webMessageHooked)
                    {
                        await web.EnsureCoreWebView2Async();
                        HookWebMessageBridge(web);
                    }
                }
            }
            else
            {
                if (FindName("CodeWebView") is WebView2 web && FindName("FileTextEditorContainer") is FrameworkElement box)
                {
                    web.Visibility = Visibility.Collapsed;
                    box.Visibility = Visibility.Visible;
                }
            }
        }

        private async Task RenderEditorContentAsync()
        {
            if (_useWebEditor)
            {
                await EnsureEditorSurfaceAsync();
                // If we have an open streaming reader, just ensure surface and return (streaming handles delivery)
                if (_editorStreamReader != null)
                {
                    return;
                }
                if (_virtualizedMode)
                {
                    // Virtualized mode handles content through its own pipeline
                    return;
                }
                var content = ViewModel.FileContent ?? string.Empty;
                await SendContentToWebViewAsync(content);
                await InvokeWebEditor("setLanguage", ViewModel.IsJsonContext ? "json" : "plaintext");
                await InvokeWebEditor("setFontFamily", ViewModel.SelectedFontFamily);
                await InvokeWebEditor("setFontSize", ViewModel.EditorFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await InvokeWebEditor("setWordWrap", ViewModel.IsWordWrapEnabled ? "true" : "false");
            }
            else
            {
                // Apply font settings to TextBox and lock state
                if (FindName("FileTextEditor") is TextBox editor)
                {
                    editor.FontFamily = new FontFamily(ViewModel.SelectedFontFamily);
                    editor.FontSize = ViewModel.EditorFontSize;
                    editor.TextWrapping = ViewModel.IsWordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    var inUse = ViewModel.SaveInfo?.Path is string sp && await IsSaveInUseAsync(sp);
                    editor.IsReadOnly = inUse;
                }
            }
        }

        private async Task InvokeWebEditor(string command, string arg)
        {
            if (FindName("CodeWebView") is WebView2 web && web.CoreWebView2 != null)
            {
                var script = $"window.__editorCommand && window.__editorCommand('{command}', {System.Text.Json.JsonSerializer.Serialize(arg)});";
                await web.ExecuteScriptAsync(script);
            }
        }

        // Stream very large content into Monaco via WebMessage to avoid ExecuteScript limits
        private async Task SendContentToWebViewAsync(string content)
        {
            if (FindName("CodeWebView") is not WebView2 web) return;
            await web.EnsureCoreWebView2Async();

            // Begin message with basic options so Monaco can prepare a buffer
            var begin = new
            {
                type = "content-begin",
                totalLength = content.Length,
                readOnly = true
            };
            web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(begin));

            const int chunkSize = 200_000; // ~200k chars per chunk to keep JSON size reasonable
            for (int i = 0; i < content.Length; i += chunkSize)
            {
                var chunk = i + chunkSize <= content.Length
                    ? content.AsSpan(i, chunkSize).ToString()
                    : content.AsSpan(i, content.Length - i).ToString();

                var msg = new { type = "content-chunk", data = chunk };
                web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));
                // Yield occasionally to keep UI responsive when sending many chunks
                await Task.Yield();
            }

            var end = new { type = "content-end" };
            web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(end));
        }

        private void HookWebMessageBridge(WebView2 web)
        {
            if (_webMessageHooked) return;
            _webMessageHooked = true;
            web.CoreWebView2.WebMessageReceived += async (_, args) =>
            {
                try
                {
                    var json = args.WebMessageAsJson;
                    if (string.IsNullOrEmpty(json)) return;
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("type", out var typeEl)) return;
                    var type = typeEl.GetString();
                    if (type == "request-next")
                    {
                        if (_editorStreamReader != null)
                        {
                            var buf = new char[StreamingChunkChars];
                            var read = await _editorStreamReader.ReadBlockAsync(buf, 0, buf.Length);
                            if (read > 0)
                            {
                                var chunk = new string(buf, 0, read);
                                var msg = new { type = "content-chunk", data = chunk };
                                web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));
                            }
                            if (read == 0)
                            {
                                _editorStreamingCompleted = true;
                                var end = new { type = "content-end" };
                                web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(end));
                                _editorStreamReader.Dispose();
                                _editorStreamReader = null;
                                _editorStreamingPath = null;
                            }
                        }
                        else if (_virtualizedMode)
                        {
                            await SendNextVirtualPageAsync(web);
                        }
                    }
                    else if (type == "request-reset")
                    {
                        CleanupEditorStreaming();
                    }
                    else if (type == "scroll-top")
                    {
                        if (_virtualizedMode)
                        {
                            await PrependVirtualPageIfNeededAsync(web);
                        }
                    }
                }
                catch { }
            };
        }

        private void CleanupEditorStreaming()
        {
            try { _editorStreamReader?.Dispose(); } catch { }
            _editorStreamReader = null;
            _editorStreamingPath = null;
            _editorStreamingCompleted = false;
            _virtualizedMode = false; _virtualPages.Clear(); _virtualPageLineCounts.Clear(); _virtualLineBase = 0;
        }

        // Initialize virtualized window: read initial N pages and send to web
        private async Task InitializeVirtualizedWindowAsync(string pathToLoad, FileInfo info)
        {
            try
            {
                CleanupEditorStreaming();
                await EnsureEditorSurfaceAsync();
                if (FindName("CodeWebView") is not WebView2 web) return;
                await web.EnsureCoreWebView2Async();

                using var fs = new FileStream(pathToLoad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                _virtualPages.Clear(); _virtualPageLineCounts.Clear(); _virtualLineBase = 0;

                var pageBuilder = new System.Text.StringBuilder(256 * 1024);
                int lineInPage = 0;
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    pageBuilder.AppendLine(line);
                    lineInPage++;
                    if (lineInPage >= VirtualPageLines)
                    {
                        _virtualPages.Add(pageBuilder.ToString());
                        _virtualPageLineCounts.Add(lineInPage);
                        pageBuilder.Clear(); lineInPage = 0;
                        if (_virtualPages.Count >= VirtualMaxPagesInMemory) break;
                    }
                }
                if (lineInPage > 0)
                {
                    _virtualPages.Add(pageBuilder.ToString());
                    _virtualPageLineCounts.Add(lineInPage);
                }

                var begin = new { type = "virt-begin", readOnly = true, lineBase = _virtualLineBase };
                web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(begin));
                foreach (var page in _virtualPages)
                {
                    var msg = new { type = "virt-append", data = page };
                    web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));
                }
                var end = new { type = "virt-end" };
                web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(end));

                // If file has remaining lines, keep a stream session for subsequent request-next to fetch more pages
                if (!sr.EndOfStream)
                {
                    var fs2 = new FileStream(pathToLoad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _editorStreamReader = new StreamReader(fs2);
                    // Quickly skip the part we've already loaded
                    foreach (var page in _virtualPages)
                    {
                        // Skipping same byte count is unreliable (encoding newlines), skip line by line here
                        for (int i = 0; i < _virtualPageLineCounts[_virtualPages.IndexOf(page)]; i++)
                        {
                            await _editorStreamReader.ReadLineAsync();
                        }
                    }
                }

                // Update UI statistics: character count estimated using file size, line count approximated as lineBase + loaded lines
                _originalFileContent = string.Empty;
                ViewModel.FileContent = string.Empty;
                ViewModel.IsFileModified = false;
                var loadedLines = _virtualPageLineCounts.Sum();
                ViewModel.EditorLineCount = $"Lines: ~{loadedLines}+";
                ViewModel.EditorCharCount = $"Chars: {info.Length}";
                ViewModel.CurrentCursorPosition = "";
                ViewModel.EditorLineBase = _virtualLineBase > 0 ? $"Base: {_virtualLineBase}" : string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing virtualized window: {ex.Message}");
                _virtualizedMode = false;
            }
        }

        private async Task SendNextVirtualPageAsync(WebView2 web)
        {
            if (_editorStreamReader == null) return;
            try
            {
                var sb = new System.Text.StringBuilder(256 * 1024);
                int count = 0;
                string? line;
                while (count < VirtualPageLines && (line = await _editorStreamReader.ReadLineAsync()) != null)
                {
                    sb.AppendLine(line);
                    count++;
                }
                if (count > 0)
                {
                    _virtualPages.Add(sb.ToString());
                    _virtualPageLineCounts.Add(count);
                    var msg = new { type = "virt-append", data = sb.ToString() };
                    web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(msg));

                    // Maintain sliding window
                    if (_virtualPages.Count > VirtualMaxPagesInMemory)
                    {
                        var removedLines = _virtualPageLineCounts[0];
                        _virtualPages.RemoveAt(0);
                        _virtualPageLineCounts.RemoveAt(0);
                        _virtualLineBase += removedLines;
                        var evict = new { type = "virt-evict-top", lines = removedLines };
                        web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(evict));
                        // update status bar base line
                        ViewModel.EditorLineBase = _virtualLineBase > 0 ? $"Base: {_virtualLineBase}" : string.Empty;
                    }
                }
                else
                {
                    var end = new { type = "virt-end" };
                    web.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(end));
                    _editorStreamReader.Dispose();
                    _editorStreamReader = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending next virtual page: {ex.Message}");
            }
        }

        private async Task PrependVirtualPageIfNeededAsync(WebView2 web)
        {
            // Simplified implementation: currently does not support prepending from head (needs reverse reading maintenance), can be extended later
            await Task.CompletedTask;
        }

        private static string GetMonacoHostHtml(bool useStatic)
        {
            // Build HTML using a verbatim string with placeholders to avoid C# interpolation/brace issues
            // When using static assets, we rely on WebView2's virtual host mapping: SetVirtualHostNameToFolderMapping("app", baseDir, ...)
            // Thus the URLs must be https://app/... not app://...
            var loaderSrc = useStatic ? "https://app/monaco/vs/loader.js" : "https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.48.0/min/vs/loader.min.js";
            var vsPath = useStatic ? "https://app/monaco/vs" : "https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.48.0/min/vs";

            var html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>html,body,#c{height:100%;margin:0;padding:0;overflow:hidden}</style>
<script src='__LOADER__'></script>
</head>
<body>
<div id='c'></div>
<script>
let editor; let __buffer = []; let __expected = 0; let __received = 0; let __requesting = false;
// Virtualized mode buffers
let __virtMode = false; let __virtLinesBase = 0;
function init(){
    require.config({ paths: { 'vs': '__VSPATH__' } });
    require(['vs/editor/editor.main'], function(){
        editor = monaco.editor.create(document.getElementById('c'), { value: '', language: 'plaintext', theme: 'vs', automaticLayout: true, readOnly: true });
        window.__editorCommand = function(cmd, arg){
            if(cmd==='setLanguage') monaco.editor.setModelLanguage(editor.getModel(), arg);
            if(cmd==='setFontFamily') editor.updateOptions({ fontFamily: arg });
            if(cmd==='setFontSize') editor.updateOptions({ fontSize: parseFloat(arg) });
            if(cmd==='setWordWrap') editor.updateOptions({ wordWrap: (arg==='true'?'on':'off') });
            if(cmd==='findNext'){ editor.getAction('actions.find').run(); }
            if(cmd==='replaceNext'){ editor.getAction('editor.action.startFindReplaceAction').run(); }
            if(cmd==='minify'){ /* no-op placeholder */ }
        };

        function requestNext(){
            if(!window.chrome || !window.chrome.webview) return;
            if(__requesting) return; __requesting = true;
            window.chrome.webview.postMessage({ type: 'request-next' });
            // allow next request after a tiny delay to debounce
            setTimeout(()=>{ __requesting = false; }, 50);
        }

        if(window.chrome && window.chrome.webview){
            window.chrome.webview.addEventListener('message', e => {
                const msg = e.data;
                if(!msg || !msg.type) return;
                if(msg.type==='content-begin'){
                    __buffer = []; __expected = msg.totalLength||0; __received = 0;
                    if(typeof msg.readOnly==='boolean') editor.updateOptions({readOnly: msg.readOnly});
                } else if(msg.type==='content-chunk'){
                    if(typeof msg.data==='string'){ __buffer.push(msg.data); __received += msg.data.length; }
                } else if(msg.type==='content-end'){
                    const text = __buffer.join('');
                    editor.setValue(text);
                    __buffer = []; __expected = 0; __received = 0;
                } else if(msg.type==='virt-begin'){
                    __virtMode = true; __buffer = []; __expected = 0; __received = 0; __virtLinesBase = msg.lineBase||0;
                    editor.setValue('');
                    if(typeof msg.readOnly==='boolean') editor.updateOptions({readOnly: msg.readOnly});
                } else if(msg.type==='virt-append'){
                    if(__virtMode){
                        const current = editor.getValue();
                        editor.setValue(current + msg.data);
                        setTimeout(()=>{ requestNext(); }, 0);
                    }
                } else if(msg.type==='virt-evict-top'){
                    if(__virtMode){
                        const toRemove = msg.lines||0; if(toRemove>0){
                            // Remove first N lines using applyEdits
                            const model = editor.getModel();
                            // Build a range from line 1, column 1 to line (toRemove+1) column 1
                            const endLine = Math.min(model.getLineCount(), toRemove+1);
                            const range = new monaco.Range(1,1, endLine, 1);
                            editor.executeEdits('virt-evict', [{ range, text: '' }]);
                            __virtLinesBase += toRemove;
                        }
                    }
                } else if(msg.type==='virt-end'){
                    // no-op
                }
            });
            // Request the next chunk after first paint to kick off streaming
            requestNext();
            // Also request more when scrolled near bottom
            editor.onDidScrollChange((e)=>{
                const view = editor.getScrollTop();
                const max = editor.getScrollHeight() - editor.getLayoutInfo().height;
                if(max - view < 200) requestNext();
                if(view < 200 && window.chrome && window.chrome.webview){ window.chrome.webview.postMessage({ type: 'scroll-top' }); }
            });
        }
    });
}
if(document.readyState==='complete') init(); else window.addEventListener('load', init);
</script>
</body>
</html>";

            return html.Replace("__LOADER__", loaderSrc).Replace("__VSPATH__", vsPath);
        }
        #endregion

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            try
            {
                if (ViewModel.SaveInfo?.Path is string sp)
                {
                    _sessionLockMonitorService.SessionEnded -= OnSessionEnded;
                    _sessionLockMonitorService.SessionInUseChanged -= OnSessionInUseChanged;
                    _sessionLockMonitorService.StopMonitoring(sp);
                }
            }
            catch { }

            // Clean up timer when leaving the page
            StopChangeDetectionUpdates();
        }

        private void OnPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnSessionInUseChanged(object? sender, SessionInUseChangedEventArgs e)
        {
            var currentPath = ViewModel.SaveInfo?.Path;
            if (string.IsNullOrEmpty(currentPath) || !string.Equals(currentPath, e.SavePath, StringComparison.OrdinalIgnoreCase))
                return;

            DispatcherQueue.TryEnqueue(() => UpdateSessionLockInfoBar(e.InUse));
        }

        private void UpdateSessionLockInfoBar(bool inUse)
        {
            if (FindName("SessionLockInfoBar") is InfoBar ib)
            {
                ib.IsOpen = inUse;
            }
        }

        private async Task<int> CountRegionChunkSnbtAsync(string savePath, string relativeMcaPath)
        {
            try
            {
                var rxrz = Path.GetFileNameWithoutExtension(relativeMcaPath); // e.g., r.x.z
                var outDir = Path.Combine(savePath, "GitMC", "region", rxrz + ".mca");
                if (!Directory.Exists(outDir))
                {
                    // Fallback: if SNBT not yet exported, estimate using chunk listing
                    var fullMca = Path.Combine(savePath, relativeMcaPath);
                    if (File.Exists(fullMca))
                    {
                        try
                        {
                            var chunks = await ServiceFactory.Services.Nbt.ListChunksInRegionAsync(fullMca);
                            return chunks.Count(c => c.IsValid);
                        }
                        catch { }
                    }
                    return 0;
                }
                return Directory.GetFiles(outDir, "chunk_*.snbt", SearchOption.TopDirectoryOnly).Length;
            }
            catch { return 0; }
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
