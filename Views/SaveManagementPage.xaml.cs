using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using GitMC.Constants;
using GitMC.Extensions;
using GitMC.Helpers;
using GitMC.Models;
using GitMC.Services;
using GitMC.Utils;
using GitMC.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

namespace GitMC.Views;

public sealed partial class SaveManagementPage : Page, INotifyPropertyChanged
{
    private readonly ManagedSaveService _managedSaveService;
    private readonly IMinecraftAnalyzerService _minecraftAnalyzerService;
    private readonly IServiceAggregator _services;
    private ManagedSaveInfo? _currentInitializingSave;

    // Cancellation support
    private CancellationTokenSource? _initCancellationTokenSource;

    // Initialize steps collection for UI binding
    private ObservableCollection<SaveInitStep> _initSteps = new();

    public SaveManagementPage()
    {
        InitializeComponent();

        // Use ServiceFactory to get services
        _services = ServiceFactory.Services;
        _minecraftAnalyzerService = ServiceFactory.MinecraftAnalyzer;
        _managedSaveService = new ManagedSaveService(_services.DataStorage);

        ViewModel = new SaveManagementViewModel();
        DataContext = this;
        Loaded += SaveManagementPage_Loaded;
    }

    public SaveManagementViewModel ViewModel { get; }

    // Property for XAML binding to initialization steps
    public ObservableCollection<SaveInitStep> InitSteps
    {
        get => _initSteps;
        set
        {
            _initSteps = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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
            ViewModel.IsLoading = true;
            List<ManagedSaveInfo> managedSaves = await _managedSaveService.GetManagedSaves();
            ViewModel.UpdateManagedSaves(managedSaves);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load managed saves: {ex.Message}");
        }
        finally
        {
            ViewModel.IsLoading = false;
        }
    }

    private void SaveCard_OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ManagedSaveInfo saveInfo)
            try
            {
                // Open the save directory in File Explorer
                var psi = new ProcessStartInfo { FileName = saveInfo.OriginalPath, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open save directory: {ex.Message}");
                // Could show error dialog here
            }
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
            Text = "Git Status:", FontSize = 14, FontWeight = FontWeights.SemiBold, UseLayoutRounding = true
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
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
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
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
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
            List<ManagedSaveInfo> managedSaves = await _managedSaveService.GetManagedSaves().ConfigureAwait(false);

            // Update the statistics in the status bar
            var totalSavesCount = FindName("TotalSavesCount") as TextBlock;
            var savesWithChangesCount = FindName("SavesWithChangesCount") as TextBlock;
            var remoteUpdatesCount = FindName("RemoteUpdatesCount") as TextBlock;
            var gitStatusText = FindName("GitStatusText") as TextBlock;

            // Switch to UI thread for updates
            DispatcherQueue.TryEnqueue(() =>
            {
                if (totalSavesCount != null)
                    totalSavesCount.Text = managedSaves.Count.ToString(CultureInfo.InvariantCulture);

                if (savesWithChangesCount != null)
                {
                    int changesCount = managedSaves.Count(s => s.HasPendingChanges);
                    savesWithChangesCount.Text = changesCount.ToString(CultureInfo.InvariantCulture);
                }

                if (remoteUpdatesCount != null)
                {
                    int updatesCount = managedSaves.Sum(s => s.PendingPullCount);
                    remoteUpdatesCount.Text = updatesCount.ToString(CultureInfo.InvariantCulture);
                }

                if (gitStatusText != null)
                {
                    int gitInitializedSaves = managedSaves.Count(s => s.IsGitInitialized);
                    gitStatusText.Text = gitInitializedSaves > 0 ? "Ready" : "Not Configured";
                }

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
            return _managedSaveService.GetManagedSavesCount();
        }
        catch
        {
            return 0;
        }
    }

    private string GetManagedSavesStoragePath()
    {
        return _managedSaveService.GetManagedSavesStoragePath();
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

    private async void InitializeGitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ManagedSaveInfo saveInfo)
            try
            {
                // Set up cancellation support
                _initCancellationTokenSource = new CancellationTokenSource();
                _currentInitializingSave = saveInfo;

                // Find the container elements for initialization UI
                DependencyObject? container = GetParentContainer(button);
                Button? initializeButton =
                    container != null ? FindChildByName(container, "InitializeButton") as Button : null;
                Border? initializationPanel =
                    container != null ? FindChildByName(container, "InitializationPanel") as Border : null;
                StackPanel? setupDescriptionSection = container != null
                    ? FindChildByName(container, "SetupDescriptionSection") as StackPanel
                    : null;
                ProgressBar? progressBar = container != null
                    ? FindChildByName(container, "InitializationProgressBar") as ProgressBar
                    : null;
                TextBlock? progressStepText =
                    container != null ? FindChildByName(container, "ProgressStepText") as TextBlock : null;
                ProgressRing? overallProgressRing = container != null
                    ? FindChildByName(container, "OverallProgressRing") as ProgressRing
                    : null;
                StackPanel? stepsContainer =
                    container != null ? FindChildByName(container, "StepsContainer") as StackPanel : null;

                // Initialize the steps collection
                InitSteps = _services.SaveInitialization.GetInitializationSteps();

                // Update progress bar maximum
                if (progressBar != null)
                {
                    progressBar.Maximum = InitSteps.Count;
                    progressBar.Value = 0;
                }

                // Show initialization panel and hide button and description
                if (initializeButton != null && initializationPanel != null)
                {
                    initializeButton.Visibility = Visibility.Collapsed;
                    initializationPanel.Visibility = Visibility.Visible;
                }

                // Hide the description section during initialization
                if (setupDescriptionSection != null) setupDescriptionSection.Visibility = Visibility.Collapsed;

                // Create step UI elements programmatically
                if (stepsContainer != null)
                {
                    stepsContainer.Children.Clear();
                    foreach (SaveInitStep step in InitSteps)
                    {
                        Grid stepGrid = CreateStepUI(step);
                        stepsContainer.Children.Add(stepGrid);
                    }
                }

                // Setup progress tracking
                var progress = new Progress<SaveInitStep>(step =>
                {
                    // Update the step in the collection
                    SaveInitStep? existingStep = InitSteps.FirstOrDefault(s => s.Name == step.Name);
                    if (existingStep != null)
                    {
                        existingStep.Status = step.Status;
                        existingStep.Message = step.Message;
                        existingStep.CurrentProgress = step.CurrentProgress;
                        existingStep.TotalProgress = step.TotalProgress;

                        // Update the corresponding UI element
                        if (stepsContainer != null)
                        {
                            int stepIndex = InitSteps.IndexOf(existingStep);
                            if (stepIndex >= 0 && stepIndex < stepsContainer.Children.Count)
                                UpdateStepUI(stepsContainer.Children[stepIndex] as Grid, existingStep);
                        }
                    }

                    // Update progress bar and step text with fixed logic and percentage
                    if (progressBar != null && progressStepText != null)
                    {
                        int completedSteps = InitSteps.Count(s => s.Status == SaveInitStepStatus.Completed);
                        int inProgressSteps = InitSteps.Count(s => s.Status == SaveInitStepStatus.InProgress);

                        progressBar.Value = completedSteps;

                        // Calculate overall percentage
                        double overallPercentage =
                            InitSteps.Count > 0 ? (double)completedSteps / InitSteps.Count * 100 : 0;

                        // Show current step number correctly with percentage
                        if (inProgressSteps > 0)
                        {
                            // Find the current step that's in progress
                            int currentStepIndex =
                                InitSteps.IndexOf(InitSteps.First(s => s.Status == SaveInitStepStatus.InProgress)) + 1;
                            progressStepText.Text =
                                $"Step {currentStepIndex} of {InitSteps.Count} ({overallPercentage:F0}%)";
                        }
                        else if (completedSteps == InitSteps.Count)
                        {
                            progressStepText.Text = $"Completed {InitSteps.Count} of {InitSteps.Count} (100%)";
                        }
                        else
                        {
                            progressStepText.Text =
                                $"Step {completedSteps + 1} of {InitSteps.Count} ({overallPercentage:F0}%)";
                        }
                    }
                });

                // Start initialization
                bool success = await _services.SaveInitialization.InitializeSaveAsync(saveInfo.OriginalPath, progress);

                // Stop the overall progress ring
                if (overallProgressRing != null) overallProgressRing.IsActive = false;

                if (success)
                {
                    // Update the save info to reflect Git initialization
                    saveInfo.IsGitInitialized = true;
                    saveInfo.Branch = "main"; // Set default branch
                    saveInfo.CommitCount = 1; // Initial commit
                    await _managedSaveService.UpdateManagedSave(saveInfo);

                    // Refresh the saves list to update UI
                    await LoadManagedSaves();

                    FlyoutHelper.ShowSuccessFlyout(button, "Git Initialized",
                        $"Git repository has been successfully initialized for '{saveInfo.Name}'!");
                }
                else
                {
                    // Reset UI state on failure
                    if (initializeButton != null && initializationPanel != null)
                    {
                        initializeButton.Visibility = Visibility.Visible;
                        initializationPanel.Visibility = Visibility.Collapsed;
                    }

                    // Show the description section again
                    if (setupDescriptionSection != null) setupDescriptionSection.Visibility = Visibility.Visible;

                    FlyoutHelper.ShowErrorFlyout(button, "Git Initialization Failed",
                        "Failed to initialize Git repository. Please check the logs for details.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize Git: {ex.Message}");

                // Reset UI state on error
                DependencyObject? container = GetParentContainer(button);
                Button? initializeButton =
                    container != null ? FindChildByName(container, "InitializeButton") as Button : null;
                Border? initializationPanel =
                    container != null ? FindChildByName(container, "InitializationPanel") as Border : null;
                StackPanel? setupDescriptionSection = container != null
                    ? FindChildByName(container, "SetupDescriptionSection") as StackPanel
                    : null;

                if (initializeButton != null && initializationPanel != null)
                {
                    initializeButton.Visibility = Visibility.Visible;
                    initializationPanel.Visibility = Visibility.Collapsed;
                }

                // Show the description section again
                if (setupDescriptionSection != null) setupDescriptionSection.Visibility = Visibility.Visible;

                FlyoutHelper.ShowErrorFlyout(button, "Git Initialization Error",
                    $"An error occurred: {ex.Message}");
            }
    }

    private Grid CreateStepUI(SaveInitStep step)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Create a container for the icon that can hold either FontIcon or ProgressRing
        var iconContainer = new Grid
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 8, 0)
        };
        Grid.SetColumn(iconContainer, 0);
        Grid.SetRowSpan(iconContainer, 2);

        // Status Icon
        var icon = new FontIcon
        {
            FontSize = 12,
            Glyph = step.StatusIcon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // ProgressRing for in-progress status
        var progressRing = new ProgressRing
        {
            Width = 14,
            Height = 14,
            IsActive = step.IsInProgress,
            Visibility = step.IsInProgress ? Visibility.Visible : Visibility.Collapsed
        };

        // Parse color string to Color and apply to both icon and progress ring
        if (step.StatusColor.StartsWith("#"))
        {
            string colorStr = step.StatusColor.Substring(1);
            if (colorStr.Length == 6)
            {
                byte r = Convert.ToByte(colorStr.Substring(0, 2), 16);
                byte g = Convert.ToByte(colorStr.Substring(2, 2), 16);
                byte b = Convert.ToByte(colorStr.Substring(4, 2), 16);
                var brush = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                icon.Foreground = brush;
                progressRing.Foreground = brush;
            }
        }

        // Show ProgressRing only for in-progress status, FontIcon otherwise
        icon.Visibility = step.IsInProgress ? Visibility.Collapsed : Visibility.Visible;

        iconContainer.Children.Add(icon);
        iconContainer.Children.Add(progressRing);
        grid.Children.Add(iconContainer);

        // Step name text (always displayed) - use DisplayName for progress info
        var stepNameText = new TextBlock
        {
            Text = step.DisplayName,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 150, 243)), // Blue color #2196F3
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(stepNameText, 1);
        Grid.SetRow(stepNameText, 0);
        grid.Children.Add(stepNameText);

        // Progress and description text (displayed only when in progress and has info)
        var descriptionText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 100, 100, 100)), // Gray color
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(descriptionText, 1);
        Grid.SetRow(descriptionText, 1);
        grid.Children.Add(descriptionText);

        // Store references for easy updating
        grid.Tag = new
        {
            Icon = icon,
            ProgressRing = progressRing,
            StepNameText = stepNameText,
            DescriptionText = descriptionText,
            IconContainer = iconContainer
        };

        return grid;
    }

    private void UpdateStepUI(Grid? stepGrid, SaveInitStep step)
    {
        if (stepGrid?.Tag is { } tag)
        {
            PropertyInfo[] properties = tag.GetType().GetProperties();
            PropertyInfo? iconProperty = properties.FirstOrDefault(p => p.Name == "Icon");
            PropertyInfo? progressRingProperty = properties.FirstOrDefault(p => p.Name == "ProgressRing");
            PropertyInfo? stepNameTextProperty = properties.FirstOrDefault(p => p.Name == "StepNameText");
            PropertyInfo? descriptionTextProperty = properties.FirstOrDefault(p => p.Name == "DescriptionText");

            // Update icon
            if (iconProperty?.GetValue(tag) is FontIcon icon)
            {
                icon.Glyph = step.StatusIcon;
                icon.Visibility = step.IsInProgress ? Visibility.Collapsed : Visibility.Visible;

                // Update icon color
                if (step.StatusColor.StartsWith("#"))
                {
                    string colorStr = step.StatusColor.Substring(1);
                    if (colorStr.Length == 6)
                    {
                        byte r = Convert.ToByte(colorStr.Substring(0, 2), 16);
                        byte g = Convert.ToByte(colorStr.Substring(2, 2), 16);
                        byte b = Convert.ToByte(colorStr.Substring(4, 2), 16);
                        icon.Foreground = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                    }
                }
            }

            // Update progress ring
            if (progressRingProperty?.GetValue(tag) is ProgressRing progressRing)
            {
                progressRing.IsActive = step.IsInProgress;
                progressRing.Visibility = step.IsInProgress ? Visibility.Visible : Visibility.Collapsed;

                // Update progress ring color
                if (step.StatusColor.StartsWith("#"))
                {
                    string colorStr = step.StatusColor.Substring(1);
                    if (colorStr.Length == 6)
                    {
                        byte r = Convert.ToByte(colorStr.Substring(0, 2), 16);
                        byte g = Convert.ToByte(colorStr.Substring(2, 2), 16);
                        byte b = Convert.ToByte(colorStr.Substring(4, 2), 16);
                        progressRing.Foreground = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                    }
                }
            }

            // Update step name text with DisplayName (includes progress for extracting chunks)
            if (stepNameTextProperty?.GetValue(tag) is TextBlock stepNameText)
                // Use DisplayName which includes progress information for "Extracting chunks" step
                stepNameText.Text = step.DisplayName;

            // Update description text
            if (descriptionTextProperty?.GetValue(tag) is TextBlock descriptionText)
            {
                if (step.IsInProgress && !string.IsNullOrEmpty(step.Message))
                {
                    // Don't add progress info to description text anymore since it's in the title
                    descriptionText.Text = step.Message;
                    descriptionText.Visibility = Visibility.Visible;
                }
                else
                {
                    descriptionText.Visibility = Visibility.Collapsed;
                }
            }
        }
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
            await _managedSaveService.RegisterManagedSave(save);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register managed save: {ex.Message}");
        }
    }

    private FrameworkElement? FindChildByName(DependencyObject parent, string name)
    {
        if (parent == null) return null;

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject? child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement element && element.Name == name)
                return element;

            FrameworkElement? result = FindChildByName(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    private DependencyObject? GetParentContainer(DependencyObject child)
    {
        if (child == null) return null;

        // Walk up the visual tree to find the GridViewItem container
        DependencyObject? parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is GridViewItem)
                return parent;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private async void CancelInitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
            try
            {
                // Cancel the ongoing initialization
                _initCancellationTokenSource?.Cancel();

                // Find the container elements
                DependencyObject? container = GetParentContainer(button);
                Button? initializeButton =
                    container != null ? FindChildByName(container, "InitializeButton") as Button : null;
                Border? initializationPanel =
                    container != null ? FindChildByName(container, "InitializationPanel") as Border : null;
                StackPanel? setupDescriptionSection = container != null
                    ? FindChildByName(container, "SetupDescriptionSection") as StackPanel
                    : null;
                ProgressRing? overallProgressRing = container != null
                    ? FindChildByName(container, "OverallProgressRing") as ProgressRing
                    : null;

                // Stop the overall progress ring
                if (overallProgressRing != null) overallProgressRing.IsActive = false;

                // Reset UI state
                if (initializeButton != null && initializationPanel != null)
                {
                    initializeButton.Visibility = Visibility.Visible;
                    initializationPanel.Visibility = Visibility.Collapsed;
                }

                // Show the description section again
                if (setupDescriptionSection != null) setupDescriptionSection.Visibility = Visibility.Visible;

                // Revert any changes made during initialization if needed
                if (_currentInitializingSave != null)
                {
                    await RevertInitializationChanges(_currentInitializingSave);
                    _currentInitializingSave = null;
                }

                // Clean up cancellation token
                _initCancellationTokenSource?.Dispose();
                _initCancellationTokenSource = null;

                FlyoutHelper.ShowSuccessFlyout(button, "Initialization Cancelled",
                    "Git initialization has been cancelled and any changes have been reverted.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cancelling initialization: {ex.Message}");
                FlyoutHelper.ShowErrorFlyout(button, "Error",
                    "An error occurred while cancelling initialization.");
            }
    }

    private Task RevertInitializationChanges(ManagedSaveInfo saveInfo)
    {
        try
        {
            // This would revert any changes made during initialization
            // For now, we'll just ensure the .git folder is removed if it exists
            string gitPath = Path.Combine(saveInfo.OriginalPath, ".git");
            if (Directory.Exists(gitPath)) Directory.Delete(gitPath, true);

            // Remove any other GitMC-created files/folders
            string gitignorePath = Path.Combine(saveInfo.OriginalPath, ".gitignore");
            if (File.Exists(gitignorePath)) File.Delete(gitignorePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reverting changes: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
