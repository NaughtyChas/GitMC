using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI;
using GitMC.Extensions;
using GitMC.Models;
using GitMC.Services;
using GitMC.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;

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
    private readonly IGitService _gitService;
    private readonly ManagedSaveService _managedSaveService;
    private readonly IMinecraftAnalyzerService _minecraftAnalyzerService;
    private readonly NbtService _nbtService;
    private readonly IGitHubAppsService _gitHubAppsService;
    private string? _saveId;

    public SaveDetailPage()
    {
        this.InitializeComponent();

        // Use ServiceFactory to get shared service instances
        var services = ServiceFactory.Services;
        _nbtService = services.Nbt as NbtService ?? new NbtService();
        _configurationService = services.Configuration;
        _gitService = services.Git;
        _dataStorageService = services.DataStorage;
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
                foreach (var commit in commitInfos)
                {
                    ViewModel.RecentCommits.Add(commit);
                }
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
        string token = _configurationService.GitHubAccessToken;
        DateTime tokenTime = _configurationService.GitHubAccessTokenTimestamp;

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
        if (ViewModel.SaveInfo == null || !ViewModel.SaveInfo.IsGitHubLinked || string.IsNullOrWhiteSpace(ViewModel.SaveInfo.GitHubRepositoryName))
        {
            noRepoPanel.Visibility = Visibility.Visible;
            return;
        }

        // Repo linked, show info
        linkedPanel.Visibility = Visibility.Visible;
        // Username, avatar
        var user = await _gitHubAppsService.GetUserAsync(token);
        if (FindName("GitHubAvatar") is Image avatar && user?.AvatarUrl is string avatarUrl)
            avatar.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(avatarUrl));
        if (FindName("GitHubUserNameText") is TextBlock userNameText)
            userNameText.Text = user?.Login ?? "";
        // Repo link
        if (FindName("GitHubRepoLink") is HyperlinkButton repoLink)
        {
            repoLink.Content = ViewModel.SaveInfo.GitHubRepositoryName;
            repoLink.NavigateUri = new Uri($"https://github.com/{user?.Login}/{ViewModel.SaveInfo.GitHubRepositoryName}");
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
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }
    }

    // Create and link repository
    private async void CreateAndLinkRepo_Click(object sender, RoutedEventArgs e)
    {
        if (FindName("GitHubRepoNameBox") is TextBox nameBox &&
            FindName("GitHubRepoDescBox") is TextBox descBox &&
            FindName("GitHubRepoPrivateBox") is CheckBox privateBox)
        {
            string repoName = nameBox.Text.Trim();
            string desc = descBox.Text.Trim();
            bool isPrivate = privateBox.IsChecked ?? true;
            if (string.IsNullOrWhiteSpace(repoName))
            {
                await ShowInfoDialog("Repository name is required.");
                return;
            }
            string token = _configurationService.GitHubAccessToken;
            bool created = await _gitHubAppsService.CreateRepositoryAsync(token, repoName, isPrivate, desc);
            if (created)
            {
                // Save binding info to the save-specific configuration
                if (ViewModel.SaveInfo != null)
                {
                    ViewModel.SaveInfo.GitHubRepositoryName = repoName;
                    ViewModel.SaveInfo.GitHubIsPrivateRepository = isPrivate;
                    ViewModel.SaveInfo.GitHubRepositoryDescription = desc;
                    ViewModel.SaveInfo.GitHubDefaultBranch = "main";
                    string user = _configurationService.GitHubUsername;
                    string remoteUrl = $"https://github.com/{user}/{repoName}.git";
                    ViewModel.SaveInfo.GitHubRemoteUrl = remoteUrl;
                    ViewModel.SaveInfo.IsGitHubLinked = true;

                    // Update the save info in storage
                    await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);

                    // Add remote to local repo
                    if (ViewModel.SaveInfo.IsGitInitialized && !string.IsNullOrEmpty(ViewModel.SaveInfo.OriginalPath))
                    {
                        await _gitService.AddRemoteAsync(ViewModel.SaveInfo.OriginalPath, "origin", remoteUrl);
                    }
                }
                await LoadRemoteInfoAsync();
            }
            else
            {
                await ShowInfoDialog("Failed to create repository on GitHub.");
            }
        }
    }

    // Link existing repository (input dialog)
    private async void LinkExistingRepo_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            Title = "Link Existing GitHub Repository",
            PrimaryButtonText = "Link",
            CloseButtonText = "Cancel"
        };
        StackPanel panel = new() { Spacing = 8 };
        TextBox repoBox = new() { PlaceholderText = "Repository name (e.g. my-mc-save)" };
        TextBox branchBox = new() { PlaceholderText = "Default branch (e.g. main)", Text = "main" };
        panel.Children.Add(new TextBlock { Text = "Repository Name" });
        panel.Children.Add(repoBox);
        panel.Children.Add(new TextBlock { Text = "Default Branch" });
        panel.Children.Add(branchBox);
        dialog.Content = panel;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            string repoName = repoBox.Text.Trim();
            string branch = branchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(repoName))
            {
                await ShowInfoDialog("Repository name is required.");
                return;
            }

            // Save to save-specific configuration
            if (ViewModel.SaveInfo != null)
            {
                string user = _configurationService.GitHubUsername;
                string remoteUrl = $"https://github.com/{user}/{repoName}.git";
                ViewModel.SaveInfo.GitHubRepositoryName = repoName;
                ViewModel.SaveInfo.GitHubDefaultBranch = branch;
                ViewModel.SaveInfo.GitHubRemoteUrl = remoteUrl;
                ViewModel.SaveInfo.IsGitHubLinked = true;

                // Update the save info in storage
                await _managedSaveService.UpdateManagedSave(ViewModel.SaveInfo);

                // Add remote to local repo
                if (ViewModel.SaveInfo.IsGitInitialized && !string.IsNullOrEmpty(ViewModel.SaveInfo.OriginalPath))
                {
                    await _gitService.AddRemoteAsync(ViewModel.SaveInfo.OriginalPath, "origin", remoteUrl);
                }
            }
            await LoadRemoteInfoAsync();
        }
    }

    private async Task ShowInfoDialog(string message)
    {
        ContentDialog dialog = new()
        {
            Title = "Info",
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private void UpdateOverviewUI()
    {
        if (ViewModel.SaveInfo == null) return;

        // Update Current Status
        UpdateCurrentStatus();

        // Update World Information
        UpdateWorldInformation();

        // Update Statistics
        UpdateStatistics();

        // Update Remote Status
        UpdateRemoteStatus();
    }

    private void UpdateCurrentStatus()
    {
        var saveInfo = ViewModel.SaveInfo;
        if (saveInfo == null) return;

        // Update InfoBar with status information
        if (FindName("StatusInfoBar") is InfoBar statusInfoBar)
        {
            statusInfoBar.Title = GetStatusTitle(saveInfo);
            statusInfoBar.Message = GetStatusDescription(saveInfo);
            statusInfoBar.Severity = GetStatusSeverity(saveInfo);
            statusInfoBar.IsOpen = true;
        }

        // Update current branch display
        if (FindName("CurrentBranchText") is TextBlock branchText)
            branchText.Text = saveInfo.Branch ?? "main";
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

    private void UpdateWorldInformation()
    {
        var saveInfo = ViewModel.SaveInfo;
        if (saveInfo == null) return;

        if (FindName("WorldSizeText") is TextBlock sizeText)
            sizeText.Text = FormatFileSize(saveInfo.Size);

        if (FindName("GameVersionText") is TextBlock versionText)
            versionText.Text = saveInfo.GameVersion ?? "Unknown";

        if (FindName("WorldLastModifiedText") is TextBlock modifiedText)
            modifiedText.Text = saveInfo.LastModifiedFormatted;

        if (FindName("AddedDateText") is TextBlock addedText)
            addedText.Text = saveInfo.AddedDate.ToString("MMM dd, yyyy");
    }

    private void UpdateStatistics()
    {
        var saveInfo = ViewModel.SaveInfo;
        if (saveInfo == null) return;

        if (FindName("TotalCommitsText") is TextBlock commitsText)
            commitsText.Text = saveInfo.CommitCount.ToString();

        if (FindName("BranchesCountText") is TextBlock branchesText)
            branchesText.Text = "1"; // TODO: Add branch count to SaveInfo

        if (FindName("RepositoryAgeText") is TextBlock ageText)
        {
            var age = DateTime.Now - saveInfo.AddedDate;
            ageText.Text = FormatTimeSpan(age);
        }

        if (FindName("WorkingTreeStatusText") is TextBlock treeStatusText)
        {
            treeStatusText.Text = saveInfo.CurrentStatus switch
            {
                ManagedSaveInfo.SaveStatus.Clear => "Clean",
                ManagedSaveInfo.SaveStatus.Modified => "Modified",
                ManagedSaveInfo.SaveStatus.Conflict => "Conflicts",
                _ => "Unknown"
            };
        }
    }

    private void UpdateRemoteStatus()
    {
        var saveInfo = ViewModel.SaveInfo;
        if (saveInfo == null) return;

        if (FindName("PushStatusDisplay") is TextBlock pushStatus)
        {
            pushStatus.Text = saveInfo.PendingPushCount > 0
                ? $"{saveInfo.PendingPushCount} commits to push"
                : "Up to date";
        }

        if (FindName("PullStatusDisplay") is TextBlock pullStatus)
        {
            pullStatus.Text = saveInfo.PendingPullCount > 0
                ? $"{saveInfo.PendingPullCount} commits to pull"
                : "Up to date";
        }

        if (FindName("RemoteUrlText") is TextBlock remoteUrlText)
        {
            remoteUrlText.Text = saveInfo.GitHubRemoteUrl ?? "Not configured";
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 365)
            return $"{(int)(timeSpan.TotalDays / 365)} years";
        if (timeSpan.TotalDays >= 30)
            return $"{(int)(timeSpan.TotalDays / 30)} months";
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays} days";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours} hours";
        return $"{(int)timeSpan.TotalMinutes} minutes";
    }

    private void ViewAllCommits_Click(object sender, RoutedEventArgs e)
    {
        // Switch to History tab to show all commits
        if (FindName("TabSelector") is SelectorBar tabSelector &&
            FindName("HistoryTab") is SelectorBarItem historyTab)
        {
            tabSelector.SelectedItem = historyTab;
        }
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
                // TODO: Implement git commit functionality
                // For now, just show a placeholder
                Debug.WriteLine("Committing changes...");

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error committing changes: {ex.Message}");
            // TODO: Show error message to user
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
            if (FindName("BranchComboBox") is ComboBox branchComboBox)
            {
                branchComboBox.IsDropDownOpen = false;
            }
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
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path))
            {
                // TODO: Implement file scanning functionality
                Debug.WriteLine("Scanning save files...");

                // Refresh save info
                await LoadSaveDetailAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error scanning save: {ex.Message}");
            // TODO: Show error message to user
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
            if (!string.IsNullOrEmpty(ViewModel.SaveInfo?.Path) && FindName("BranchComboBox") is ComboBox branchComboBox)
            {
                // TODO: Get actual branches from git service
                var branches = new List<string> { "main", "develop", "feature/new-feature" };

                var comboBoxItems = new List<BranchComboBoxItem>();

                // Add actual branches
                foreach (var branch in branches)
                {
                    comboBoxItems.Add(new BranchComboBoxItem
                    {
                        DisplayName = branch,
                        BranchName = branch,
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

    private void OnPropertyChanged(string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
