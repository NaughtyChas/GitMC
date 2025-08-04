using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    private string? _saveId;

    public SaveDetailPage()
    {
        this.InitializeComponent();
        _nbtService = new NbtService();
        _configurationService = new ConfigurationService();
        _gitService = new GitService(_configurationService);
        _dataStorageService = new DataStorageService();
        _minecraftAnalyzerService = new MinecraftAnalyzerService(_nbtService);
        _managedSaveService = new ManagedSaveService(_dataStorageService);

        ViewModel = new SaveDetailViewModel();
        DataContext = this;
        // Removed Loaded event handler to prevent potential deadlocks
    }

    public SaveDetailViewModel ViewModel { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string saveId)
        {
            _saveId = saveId;

            // Initialize the page asynchronously without blocking navigation
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100); // Small delay to let navigation complete
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        await InitializePageAsync();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error initializing SaveDetailPage: {ex.Message}");
                }
            });
        }
    }

    private async Task InitializePageAsync()
    {
        try
        {
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
        // TODO: Navigate to appropriate tab content
        // For now, just show placeholder content
        switch (tabName)
        {
            case "Overview":
                // ContentFrame.Navigate(typeof(SaveOverviewTab));
                break;
            case "Files":
                // ContentFrame.Navigate(typeof(SaveFilesTab));
                break;
            case "History":
                // ContentFrame.Navigate(typeof(SaveHistoryTab));
                break;
            case "Changes":
                // ContentFrame.Navigate(typeof(SaveChangesTab));
                break;
            case "Analytics":
                // ContentFrame.Navigate(typeof(SaveAnalyticsTab));
                break;
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
