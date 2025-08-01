using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GitMC.Constants;
using GitMC.Helpers;
using GitMC.Models;
using GitMC.Services;
using GitMC.Utils;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace GitMC.Views;

public sealed partial class SaveManagementPage : Page, INotifyPropertyChanged
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private readonly NbtService _nbtService;
    private readonly IOnboardingService _onboardingService;
    private readonly IMinecraftAnalyzerService _minecraftAnalyzerService;

    public SaveManagementPage()
    {
        InitializeComponent();
        _nbtService = new NbtService();
        _gitService = new GitService();
        _configurationService = new ConfigurationService();
        _dataStorageService = new DataStorageService();
        _onboardingService = new OnboardingService(_gitService, _configurationService);
        _minecraftAnalyzerService = new MinecraftAnalyzerService(_nbtService);

        DataContext = this;
        Loaded += SaveManagementPage_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void SaveManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadManagedSaves();
            UpdateStatistics();
        }
        catch (Exception ex)
        {
            // Log error and show user-friendly message
            Debug.WriteLine($"Error loading saves: {ex.Message}");
            // Could show a message dialog here
        }
    }

    private async Task LoadManagedSaves()
    {
        try
        {
            List<ManagedSaveInfo> managedSaves = await GetManagedSaves();
            PopulateSavesList(managedSaves);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load managed saves: {ex.Message}");
        }
    }

    private async Task<List<ManagedSaveInfo>> GetManagedSaves()
    {
        var saves = new List<ManagedSaveInfo>();
        string managedSavesPath = GetManagedSavesStoragePath();

        if (!Directory.Exists(managedSavesPath)) return saves;

        string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
        foreach (string jsonFile in jsonFiles)
            try
            {
                string json = await File.ReadAllTextAsync(jsonFile);
                ManagedSaveInfo? saveInfo = JsonSerializer.Deserialize<ManagedSaveInfo>(json);
                if (saveInfo != null) saves.Add(saveInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse save info from {jsonFile}: {ex.Message}");
            }

        return saves.OrderByDescending(s => s.LastModified).ToList();
    }

    private void PopulateSavesList(List<ManagedSaveInfo> saves)
    {
        var savesGridView = FindName("SavesGridView") as GridView;
        var saveCountInfo = FindName("SaveCountInfo") as TextBlock;

        if (savesGridView == null || saveCountInfo == null) return;

        // Clear existing items
        savesGridView.Items.Clear();

        // Update save count info
        if (saves.Count == 0)
            saveCountInfo.Text = "No saves managed yet";
        else
            saveCountInfo.Text = $"{saves.Count} save{(saves.Count == 1 ? "" : "s")} managed";

        // Add real save cards as squared cards
        foreach (ManagedSaveInfo save in saves)
        {
            Border saveCard = CreateSquaredSaveCard(save);
            savesGridView.Items.Add(saveCard);
        }

        // Update statistics
        UpdateStatistics();
    }

    private Border CreateSquaredSaveCard(ManagedSaveInfo saveInfo)
    {
        var saveCard = new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["SystemControlBackgroundAltHighBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            UseLayoutRounding = true
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        }); // Row 1: Header with icon, title, path and actions
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: General information
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Separator
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Git status badges

        // Row 1: Header section
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        }); // Title and path
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Status badge
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Open button
        headerGrid.Margin = new Thickness(0, 0, 0, 16);
        Grid.SetRow(headerGrid, 0);

        // Folder icon with rounded background
        var iconContainer = new Border
        {
            Background = new SolidColorBrush(ColorConstants.IconColors.FolderBackground),
            BorderBrush = new SolidColorBrush(ColorConstants.IconColors.FolderBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 40,
            Height = 40,
            Margin = new Thickness(0, 0, 12, 0),
            UseLayoutRounding = true
        };

        var folderIcon = new FontIcon
        {
            FontSize = 18,
            Glyph = "\uE8B7", // Folder icon
            Foreground = new SolidColorBrush(ColorConstants.IconColors.FolderIcon),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };
        iconContainer.Child = folderIcon;
        Grid.SetColumn(iconContainer, 0);

        // Title and path section
        var titlePathPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var nameText = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Text = saveInfo.Name,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
            UseLayoutRounding = true
        };

        var pathText = new TextBlock
        {
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Text = saveInfo.OriginalPath ?? "Unknown path",
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            UseLayoutRounding = true
        };

        titlePathPanel.Children.Add(nameText);
        titlePathPanel.Children.Add(pathText);
        Grid.SetColumn(titlePathPanel, 1);

        // Status badge
        Border statusBadge = CreateWarningBadge("modified");
        statusBadge.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(statusBadge, 2);

        // Open button
        var openButton = new Button
        {
            Content = "Open",
            Height = 32,
            MinWidth = 60,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };
        openButton.Click += (_, _) => ShowSaveActions(saveInfo);
        Grid.SetColumn(openButton, 3);

        headerGrid.Children.Add(iconContainer);
        headerGrid.Children.Add(titlePathPanel);
        headerGrid.Children.Add(statusBadge);
        headerGrid.Children.Add(openButton);

        // Row 2: General information display
        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        infoGrid.Margin = new Thickness(0, 0, 0, 16);
        Grid.SetRow(infoGrid, 1);

        // Branch info
        StackPanel branchInfoPanel = CreateInfoPanel(null, "Assets/Icons/branch_grey.svg", "Branch", "main",
            ColorConstants.InfoPanelColors.SecondaryIconText);
        Grid.SetColumn(branchInfoPanel, 0);

        // Size info
        StackPanel sizeInfoPanel = CreateInfoPanel(null, "Assets/Icons/db_grey.svg", "Size", "2.4 GB",
            ColorConstants.InfoPanelColors.SecondaryIconText);
        Grid.SetColumn(sizeInfoPanel, 1);

        // Commits info
        StackPanel commitsInfoPanel = CreateInfoPanel(null, "Assets/Icons/commit_grey.svg", "Commits", "45",
            ColorConstants.InfoPanelColors.SecondaryIconText);
        Grid.SetColumn(commitsInfoPanel, 2);

        // Modified info
        StackPanel modifiedInfoPanel = CreateInfoPanel("\uE823", null, "Modified", "2 hours ago",
            ColorConstants.InfoPanelColors.SecondaryIconText);
        Grid.SetColumn(modifiedInfoPanel, 3);

        infoGrid.Children.Add(branchInfoPanel);
        infoGrid.Children.Add(sizeInfoPanel);
        infoGrid.Children.Add(commitsInfoPanel);
        infoGrid.Children.Add(modifiedInfoPanel);

        // Separator
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(ColorConstants.InfoPanelColors.SeparatorBackground),
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(separator, 2);

        // Row 3: Git status badges
        var gitStatusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        Grid.SetRow(gitStatusPanel, 3);

        var gitStatusHeader = new TextBlock
        {
            Text = "Git Status:",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            UseLayoutRounding = true
        };
        gitStatusPanel.Children.Add(gitStatusHeader);

        // Push badge
        Border pushBadge = CreateGitStatusBadge("\uE898", "1 to push", ColorConstants.BadgeColors.GitText);
        gitStatusPanel.Children.Add(pushBadge);

        // Pull badge
        Border pullBadge = CreateGitStatusBadge("\uE896", "2 to pull", ColorConstants.BadgeColors.GitText);
        gitStatusPanel.Children.Add(pullBadge);

        mainGrid.Children.Add(headerGrid);
        mainGrid.Children.Add(infoGrid);
        mainGrid.Children.Add(separator);
        mainGrid.Children.Add(gitStatusPanel);
        saveCard.Child = mainGrid;

        return saveCard;
    }

    private StackPanel CreateInfoPanel(string? iconGlyph, string? iconPath, string title, string data, Color iconColor)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!string.IsNullOrEmpty(iconPath))
        {
            if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var imageIcon = new ImageIcon
                {
                    Source = new SvgImageSource(new Uri($"ms-appx:///{iconPath.TrimStart('/', '.')}")),
                    Foreground = new SolidColorBrush(iconColor),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    UseLayoutRounding = true
                };
                panel.Children.Add(imageIcon);
            }
            else
            {
                var image = new Image
                {
                    Source = new BitmapImage(new Uri($"ms-appx:///{iconPath.TrimStart('/', '.')}")),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    UseLayoutRounding = true
                };
                panel.Children.Add(image);
            }
        }
        else if (!string.IsNullOrEmpty(iconGlyph))
        {
            var fontIcon = new FontIcon
            {
                FontSize = 16,
                Glyph = iconGlyph,
                Foreground = new SolidColorBrush(iconColor),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };
            panel.Children.Add(fontIcon);
        }

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleText = new TextBlock
        {
            FontSize = 11,
            Text = title,
            Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Margin = new Thickness(0, 0, 0, 2),
            UseLayoutRounding = true
        };

        var dataText = new TextBlock
        {
            FontSize = 13,
            Text = data,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseHighBrush"],
            UseLayoutRounding = true
        };

        textPanel.Children.Add(titleText);
        textPanel.Children.Add(dataText);
        panel.Children.Add(textPanel);

        return panel;
    }

    private Border CreateGitStatusBadge(string iconGlyph, string text, Color color)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(ColorConstants.BadgeColors.GitBackground),
            BorderBrush = new SolidColorBrush(ColorConstants.BadgeColors.GitBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            UseLayoutRounding = true
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            FontSize = 10,
            Glyph = iconGlyph,
            Foreground = new SolidColorBrush(ColorConstants.BadgeColors.GitText),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };

        var textBlock = new TextBlock
        {
            FontSize = 10,
            Text = text,
            Foreground = new SolidColorBrush(ColorConstants.BadgeColors.GitText),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };

        panel.Children.Add(icon);
        panel.Children.Add(textBlock);
        badge.Child = panel;

        return badge;
    }

    // Helper methods for creating different types of badges
    private Border CreateTypedBadge(string text, BadgeType type)
    {
        return type switch
        {
            BadgeType.Info => CreateBadge(text, ColorConstants.BadgeColors.InfoBackground,
                ColorConstants.BadgeColors.InfoBorder, ColorConstants.BadgeColors.InfoText),
            BadgeType.Warning => CreateBadge(text, ColorConstants.BadgeColors.WarningBackground,
                ColorConstants.BadgeColors.WarningBorder, ColorConstants.BadgeColors.WarningText),
            BadgeType.Success => CreateBadge(text, ColorConstants.BadgeColors.SuccessBackground,
                ColorConstants.BadgeColors.SuccessBorder, ColorConstants.BadgeColors.SuccessText),
            BadgeType.Error => CreateBadge(text, ColorConstants.BadgeColors.ErrorBackground,
                ColorConstants.BadgeColors.ErrorBorder, ColorConstants.BadgeColors.ErrorText),
            BadgeType.Git => CreateBadge(text, ColorConstants.BadgeColors.GitBackground,
                ColorConstants.BadgeColors.GitBorder, ColorConstants.BadgeColors.GitText),
            _ => CreateBadge(text, ColorConstants.BadgeColors.InfoBackground,
                ColorConstants.BadgeColors.InfoBorder, ColorConstants.BadgeColors.InfoText)
        };
    }

    private Border CreateInfoBadge(string text)
    {
        return CreateBadge(text, ColorConstants.BadgeColors.InfoBackground,
            ColorConstants.BadgeColors.InfoBorder, ColorConstants.BadgeColors.InfoText);
    }

    private Border CreateWarningBadge(string text)
    {
        return CreateBadge(text, ColorConstants.BadgeColors.WarningBackground,
            ColorConstants.BadgeColors.WarningBorder, ColorConstants.BadgeColors.WarningText);
    }

    private Border CreateSuccessBadge(string text)
    {
        return CreateBadge(text, ColorConstants.BadgeColors.SuccessBackground,
            ColorConstants.BadgeColors.SuccessBorder, ColorConstants.BadgeColors.SuccessText);
    }

    private Border CreateErrorBadge(string text)
    {
        return CreateBadge(text, ColorConstants.BadgeColors.ErrorBackground,
            ColorConstants.BadgeColors.ErrorBorder, ColorConstants.BadgeColors.ErrorText);
    }

    private Border CreateBadge(string text, Color backgroundColor, Color borderColor, Color textColor)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(backgroundColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = new SolidColorBrush(textColor),
            FontWeight = FontWeights.SemiBold,
            UseLayoutRounding = true
        };

        badge.Child = textBlock;
        return badge;
    }

    private void ShowSaveActions(ManagedSaveInfo saveInfo)
    {
        FlyoutHelper.ShowSuccessFlyout(null, "Save Actions",
            $"Actions for '{saveInfo.Name}' will be available soon!");
    }

    private async void UpdateStatistics()
    {
        try
        {
            List<ManagedSaveInfo> managedSaves = await GetManagedSaves().ConfigureAwait(false);

            // Update the statistics in the status bar
            var totalSavesCount = FindName("TotalSavesCount") as TextBlock;
            var savesWithChangesCount = FindName("SavesWithChangesCount") as TextBlock;
            var remoteUpdatesCount = FindName("RemoteUpdatesCount") as TextBlock;
            var gitStatusText = FindName("GitStatusText") as TextBlock;
            var totalSizeText = FindName("TotalSizeText") as TextBlock;

            // Switch to UI thread for updates
            DispatcherQueue.TryEnqueue(() =>
            {
                if (totalSavesCount != null)
                    totalSavesCount.Text = managedSaves.Count.ToString(CultureInfo.InvariantCulture);

                if (savesWithChangesCount != null)
                {
                    int changesCount = 0;
                    savesWithChangesCount.Text = changesCount.ToString(CultureInfo.InvariantCulture);
                }

                if (remoteUpdatesCount != null)
                {
                    int updatesCount = 0;
                    remoteUpdatesCount.Text = updatesCount.ToString(CultureInfo.InvariantCulture);
                }

                if (gitStatusText != null)
                {
                    int gitInitializedSaves = managedSaves.Count(s => s.IsGitInitialized);
                    gitStatusText.Text = gitInitializedSaves > 0 ? "Ready" : "Not Configured";
                }

                if (totalSizeText != null)
                    _ = Task.Run(async () =>
                    {
                        long totalSize = await CalculateTotalSize(managedSaves).ConfigureAwait(false);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            totalSizeText.Text = CommonHelpers.FormatFileSize(totalSize);
                        });
                    });

                // Update Quick Actions button states
                UpdateQuickActionButtons(managedSaves);

                // Update Recent Activity
                UpdateRecentActivity(managedSaves);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update statistics: {ex.Message}");
        }
    }

    private void UpdateQuickActionButtons(List<ManagedSaveInfo> saves)
    {
        var commitAllButton = FindName("CommitAllButton") as Button;
        var pullAllButton = FindName("PullAllButton") as Button;
        var pushAllButton = FindName("PushAllButtonSidebar") as Button;
        var pullBadge = FindName("PullBadge") as Border;
        var pushBadge = FindName("PushBadge") as Border;
        var pullBadgeText = FindName("PullBadgeText") as TextBlock;
        var pushBadgeText = FindName("PushBadgeText") as TextBlock;

        // Enable/disable buttons based on Git status
        bool hasGitSaves = saves.Any(s => s.IsGitInitialized);

        if (commitAllButton != null)
            commitAllButton.IsEnabled = hasGitSaves;

        if (pullAllButton != null)
            pullAllButton.IsEnabled = hasGitSaves;

        if (pushAllButton != null)
            pushAllButton.IsEnabled = hasGitSaves;

        int pendingPulls = 0;
        int pendingPushes = 0;

        if (pullBadge != null && pullBadgeText != null)
        {
            pullBadge.Visibility = pendingPulls > 0 ? Visibility.Visible : Visibility.Collapsed;
            pullBadgeText.Text = pendingPulls.ToString(CultureInfo.InvariantCulture);
        }

        if (pushBadge != null && pushBadgeText != null)
        {
            pushBadge.Visibility = pendingPushes > 0 ? Visibility.Visible : Visibility.Collapsed;
            pushBadgeText.Text = pendingPushes.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void UpdateRecentActivity(List<ManagedSaveInfo> saves)
    {
        var activityContainer = FindName("RecentActivityContainer") as StackPanel;
        var noActivityText = FindName("NoActivityText") as TextBlock;

        if (activityContainer == null || noActivityText == null) return;

        // Clear existing activity items (except the "no activity" text)
        var itemsToRemove = activityContainer.Children.Where(c => c != noActivityText).ToList();
        foreach (UIElement? item in itemsToRemove) activityContainer.Children.Remove(item);

        if (saves.Count == 0)
        {
            noActivityText.Visibility = Visibility.Visible;
            return;
        }

        noActivityText.Visibility = Visibility.Collapsed;

        // Add recent activity items (most recent first)
        IEnumerable<ManagedSaveInfo> recentSaves = saves.OrderByDescending(s => s.LastModified).Take(3);

        foreach (ManagedSaveInfo save in recentSaves)
        {
            Border activityItem = CreateActivityItem(save);
            activityContainer.Children.Insert(0, activityItem); // Insert at beginning
        }
    }

    private Border CreateActivityItem(ManagedSaveInfo saveInfo)
    {
        var item = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 243, 243, 243)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel();

        var titleText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Text = saveInfo.Name,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        panel.Children.Add(titleText);

        TimeSpan timeSpan = DateTime.Now - saveInfo.LastModified;
        string timeText;
        if (timeSpan.TotalDays >= 1)
            timeText = $"{(int)timeSpan.TotalDays} day{(timeSpan.TotalDays >= 2 ? "s" : "")} ago";
        else if (timeSpan.TotalHours >= 1)
            timeText = $"{(int)timeSpan.TotalHours} hour{(timeSpan.TotalHours >= 2 ? "s" : "")} ago";
        else
            timeText = "Recently added";

        var detailText = new TextBlock
        {
            FontSize = 11,
            Foreground = (SolidColorBrush)Application.Current.Resources["SystemControlForegroundBaseMediumBrush"],
            Text = $"Added to GitMC • {timeText}"
        };
        panel.Children.Add(detailText);

        item.Child = panel;
        return item;
    }

    private Task<long> CalculateTotalSize(List<ManagedSaveInfo> saves)
    {
        return Task.Run(() =>
        {
            long totalSize = 0;
            foreach (ManagedSaveInfo save in saves)
                try
                {
                    if (Directory.Exists(save.OriginalPath))
                    {
                        var directoryInfo = new DirectoryInfo(save.OriginalPath);
                        totalSize += CommonHelpers.CalculateFolderSize(directoryInfo);
                    }
                }
                catch
                {
                    // Skip if folder is inaccessible
                }

            return totalSize;
        });
    }

    private int GetManagedSavesCount()
    {
        try
        {
            string managedSavesPath = GetManagedSavesStoragePath();
            if (!Directory.Exists(managedSavesPath)) return 0;

            string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            return jsonFiles.Length;
        }
        catch
        {
            return 0;
        }
    }

    private string GetManagedSavesStoragePath()
    {
        return _dataStorageService.GetManagedSavesDirectory();
    }

    // Event handlers
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(SettingsPage));
    }

    private void CommitAllButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Commit All Changes",
            "Commit all changes feature is coming soon!");
    }

    private void PullAllButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Pull All Updates",
            "Pull all updates feature is coming soon!");
    }

    private void PushAllButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Push All Changes",
            "Push all changes feature is coming soon!");
    }

    private async void AddSaveButton_Click(object sender, RoutedEventArgs e)
    {
        var loadingPanel = FindName("LoadingPanel") as Grid;
        var loadingProgressRing = FindName("LoadingProgressRing") as ProgressBar;

        if (loadingPanel != null && loadingProgressRing != null)
        {
            loadingPanel.Visibility = Visibility.Visible;
            loadingProgressRing.IsIndeterminate = true;
        }

        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            if (App.MainWindow != null)
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                MinecraftSave? save = await _minecraftAnalyzerService.AnalyzeSaveFolder(folder.Path);
                if (save != null)
                {
                    // Add to navigation in MainWindow
                    if (App.MainWindow is MainWindow mainWindow) mainWindow.AddSaveToNavigation(save.Name, save.Path);

                    // Register this save in our managed saves system
                    await RegisterManagedSave(save);

                    // Refresh the saves list and statistics
                    await LoadManagedSaves();
                }
                else
                {
                    FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Invalid Minecraft Save",
                        "The selected folder doesn't appear to be a valid Minecraft save. A valid save should contain level.dat or level.dat_old.");
                }
            }
        }
        catch (Exception ex)
        {
            // Log error and show user-friendly message
            Debug.WriteLine($"Error adding save: {ex.Message}");
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Error Adding Save",
                "An error occurred while adding the save. Please try again.");
        }
        finally
        {
            if (loadingPanel != null && loadingProgressRing != null)
            {
                loadingPanel.Visibility = Visibility.Collapsed;
                loadingProgressRing.IsIndeterminate = false;
            }
        }
    }

    private void InitializeGitButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Git Initialization",
            "Git initialization feature is coming soon!");
    }

    private void SyncAllButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Sync All",
            "Sync all feature is coming soon!");
    }

    private void FetchAllButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Fetch All",
            "Fetch all changes feature is coming soon!");
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Create Backup",
            "Backup feature is coming soon!");
    }

    // Helper methods
    private async Task RegisterManagedSave(MinecraftSave save)
    {
        try
        {
            string managedSavesPath = GetManagedSavesStoragePath();

            if (!Directory.Exists(managedSavesPath)) Directory.CreateDirectory(managedSavesPath);

            string saveId = _minecraftAnalyzerService.GenerateSaveId(save.Name);
            string saveInfoPath = Path.Combine(managedSavesPath, $"{saveId}.json");

            var saveInfo = new ManagedSaveInfo
            {
                Id = saveId,
                Name = save.Name,
                OriginalPath = save.Path,
                AddedDate = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                GitRepository = "",
                IsGitInitialized = false
            };

            string json = JsonSerializer.Serialize(saveInfo, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(saveInfoPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register managed save: {ex.Message}");
        }
    }
}
