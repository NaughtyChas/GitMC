using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GitMC.Models;
using GitMC.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace GitMC.Views
{
    public sealed partial class SaveManagementPage : Page, INotifyPropertyChanged
    {
        private readonly NbtService _nbtService;
        private readonly IGitService _gitService;
        private readonly IConfigurationService _configurationService;
        private readonly IOnboardingService _onboardingService;
        private readonly IDataStorageService _dataStorageService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SaveManagementPage()
        {
            InitializeComponent();
            _nbtService = new NbtService();
            _gitService = new GitService();
            _configurationService = new ConfigurationService();
            _dataStorageService = new DataStorageService();
            _onboardingService = new OnboardingService(_gitService, _configurationService);

            DataContext = this;
            Loaded += SaveManagementPage_Loaded;
        }

        private async void SaveManagementPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadManagedSaves();
            UpdateStatistics();
        }

        private async Task LoadManagedSaves()
        {
            try
            {
                var managedSaves = await GetManagedSaves();
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
            var managedSavesPath = GetManagedSavesStoragePath();

            if (!Directory.Exists(managedSavesPath))
            {
                return saves;
            }

            var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(jsonFile);
                    var saveInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedSaveInfo>(json);
                    if (saveInfo != null)
                    {
                        saves.Add(saveInfo);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to parse save info from {jsonFile}: {ex.Message}");
                }
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
            {
                saveCountInfo.Text = "No saves managed yet";
            }
            else
            {
                saveCountInfo.Text = $"{saves.Count} save{(saves.Count == 1 ? "" : "s")} managed";
            }

            // Add real save cards as squared cards
            foreach (var save in saves)
            {
                var saveCard = CreateSquaredSaveCard(save);
                savesGridView.Items.Add(saveCard);
            }

            // Update statistics
            UpdateStatistics();
        }

        private Border CreateSquaredSaveCard(ManagedSaveInfo saveInfo)
        {
            var saveCard = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 225, 225, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                UseLayoutRounding = true
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Header with icon, title, path and actions
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: General information
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Separator
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Git status badges

            // Row 1: Header section
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title and path
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Status badge
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Open button
            headerGrid.Margin = new Thickness(0, 0, 0, 16);
            Grid.SetRow(headerGrid, 0);

            // Folder icon with rounded background
            var iconContainer = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 227, 242, 253)),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 200, 230, 250)),
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
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 33, 150, 243)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };
            iconContainer.Child = folderIcon;
            Grid.SetColumn(iconContainer, 0);

            // Title and path section
            var titlePathPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameText = new TextBlock
            {
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = saveInfo.Name,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 4),
                UseLayoutRounding = true
            };

            var pathText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                Text = saveInfo.OriginalPath ?? "Unknown path",
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                UseLayoutRounding = true
            };

            titlePathPanel.Children.Add(nameText);
            titlePathPanel.Children.Add(pathText);
            Grid.SetColumn(titlePathPanel, 1);

            // Status badge
            var statusBadge = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 152, 0)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                UseLayoutRounding = true
            };

            var statusText = new TextBlock
            {
                Text = "modified",
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                UseLayoutRounding = true
            };
            statusBadge.Child = statusText;
            Grid.SetColumn(statusBadge, 2);

            // Open button
            var openButton = new Button
            {
                Content = "Open",
                Height = 32,
                MinWidth = 60,
                Style = Application.Current.Resources["AccentButtonStyle"] as Style,
                VerticalAlignment = VerticalAlignment.Top,
                UseLayoutRounding = true
            };
            openButton.Click += (s, e) => ShowSaveActions(saveInfo);
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
            var branchInfoPanel = CreateInfoPanel("\uE8B5", "Branch", "main", ColorHelper.FromArgb(255, 128, 128, 128));
            Grid.SetColumn(branchInfoPanel, 0);

            // Size info
            var sizeInfoPanel = CreateInfoPanel("\uE8B5", "Size", "2.4 GB", ColorHelper.FromArgb(255, 128, 128, 128));
            Grid.SetColumn(sizeInfoPanel, 1);

            // Commits info
            var commitsInfoPanel = CreateInfoPanel("\uE8A7", "Commits", "45", ColorHelper.FromArgb(255, 128, 128, 128));
            Grid.SetColumn(commitsInfoPanel, 2);

            // Modified info
            var modifiedInfoPanel = CreateInfoPanel("\uE823", "Modified", "2 hours ago", ColorHelper.FromArgb(255, 128, 128, 128));
            Grid.SetColumn(modifiedInfoPanel, 3);

            infoGrid.Children.Add(branchInfoPanel);
            infoGrid.Children.Add(sizeInfoPanel);
            infoGrid.Children.Add(commitsInfoPanel);
            infoGrid.Children.Add(modifiedInfoPanel);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(ColorHelper.FromArgb(255, 225, 225, 225)),
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(separator, 2);

            // Row 3: Git status badges
            var gitStatusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };
            Grid.SetRow(gitStatusPanel, 3);

            // Push badge
            var pushBadge = CreateGitStatusBadge("\uE898", "1 to push", ColorHelper.FromArgb(255, 33, 150, 243));
            gitStatusPanel.Children.Add(pushBadge);

            // Pull badge
            var pullBadge = CreateGitStatusBadge("\uE896", "2 to pull", ColorHelper.FromArgb(255, 255, 152, 0));
            gitStatusPanel.Children.Add(pullBadge);

            mainGrid.Children.Add(headerGrid);
            mainGrid.Children.Add(infoGrid);
            mainGrid.Children.Add(separator);
            mainGrid.Children.Add(gitStatusPanel);
            saveCard.Child = mainGrid;

            return saveCard;
        }

        private StackPanel CreateInfoPanel(string iconGlyph, string title, string data, Windows.UI.Color iconColor)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new FontIcon
            {
                FontSize = 12,
                Glyph = iconGlyph,
                Foreground = new SolidColorBrush(iconColor),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };

            var textPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                FontSize = 10,
                Text = title,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 0, 0, 2),
                UseLayoutRounding = true
            };

            var dataText = new TextBlock
            {
                FontSize = 12,
                Text = data,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.Black),
                UseLayoutRounding = true
            };

            textPanel.Children.Add(titleText);
            textPanel.Children.Add(dataText);

            panel.Children.Add(icon);
            panel.Children.Add(textPanel);

            return panel;
        }

        private Border CreateGitStatusBadge(string iconGlyph, string text, Windows.UI.Color color)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(50, color.R, color.G, color.B)), // Light background
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
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
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };

            var textBlock = new TextBlock
            {
                FontSize = 10,
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };

            panel.Children.Add(icon);
            panel.Children.Add(textBlock);
            badge.Child = panel;

            return badge;
        }

        private string GetWorldIcon(string saveName)
        {
            // Bro just wanna remove this in the future.
            var lowerName = saveName.ToLower(System.Globalization.CultureInfo.InvariantCulture);
            if (lowerName.Contains("creative", StringComparison.InvariantCultureIgnoreCase))
                return "🎨";
            else if (lowerName.Contains("hardcore", StringComparison.InvariantCultureIgnoreCase))
                return "💀";
            else if (lowerName.Contains("spectator", StringComparison.InvariantCultureIgnoreCase))
                return "👻";
            else if (lowerName.Contains("adventure", StringComparison.InvariantCultureIgnoreCase))
                return "🗺️";
            else
                return "🌍";
        }

        private void ShowSaveActions(ManagedSaveInfo saveInfo)
        {
            // TODO: Implement save actions menu (open folder, remove, etc.)
            ShowSuccessFlyout(null, "Save Actions",
                $"Actions for '{saveInfo.Name}' will be available soon!");
        }

        private async void UpdateStatistics()
        {
            try
            {
                var managedSaves = await GetManagedSaves().ConfigureAwait(false);

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
                    {
                        totalSavesCount.Text = managedSaves.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (savesWithChangesCount != null)
                    {
                        // TODO: Implement actual Git status checking for changes
                        var changesCount = 0; // Placeholder
                        savesWithChangesCount.Text = changesCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (remoteUpdatesCount != null)
                    {
                        // TODO: Implement actual Git status checking for remote updates
                        var updatesCount = 0; // Placeholder
                        remoteUpdatesCount.Text = updatesCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    if (gitStatusText != null)
                    {
                        var gitInitializedSaves = managedSaves.Count(s => s.IsGitInitialized);
                        gitStatusText.Text = gitInitializedSaves > 0 ? "Ready" : "Not Configured";
                    }

                    if (totalSizeText != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            var totalSize = await CalculateTotalSize(managedSaves).ConfigureAwait(false);
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                totalSizeText.Text = FormatFileSize(totalSize);
                            });
                        });
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
            var hasGitSaves = saves.Any(s => s.IsGitInitialized);

            if (commitAllButton != null)
                commitAllButton.IsEnabled = hasGitSaves;

            if (pullAllButton != null)
                pullAllButton.IsEnabled = hasGitSaves;

            if (pushAllButton != null)
                pushAllButton.IsEnabled = hasGitSaves;

            // Update badges (placeholder logic)
            var pendingPulls = 0; // TODO: Get from Git status
            var pendingPushes = 0; // TODO: Get from Git status

            if (pullBadge != null && pullBadgeText != null)
            {
                pullBadge.Visibility = pendingPulls > 0 ? Visibility.Visible : Visibility.Collapsed;
                pullBadgeText.Text = pendingPulls.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (pushBadge != null && pushBadgeText != null)
            {
                pushBadge.Visibility = pendingPushes > 0 ? Visibility.Visible : Visibility.Collapsed;
                pushBadgeText.Text = pendingPushes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private void UpdateRecentActivity(List<ManagedSaveInfo> saves)
        {
            var activityContainer = FindName("RecentActivityContainer") as StackPanel;
            var noActivityText = FindName("NoActivityText") as TextBlock;

            if (activityContainer == null || noActivityText == null) return;

            // Clear existing activity items (except the "no activity" text)
            var itemsToRemove = activityContainer.Children.Where(c => c != noActivityText).ToList();
            foreach (var item in itemsToRemove)
            {
                activityContainer.Children.Remove(item);
            }

            if (saves.Count == 0)
            {
                noActivityText.Visibility = Visibility.Visible;
                return;
            }

            noActivityText.Visibility = Visibility.Collapsed;

            // Add recent activity items (most recent first)
            var recentSaves = saves.OrderByDescending(s => s.LastModified).Take(3);

            foreach (var save in recentSaves)
            {
                var activityItem = CreateActivityItem(save);
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
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = saveInfo.Name,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(titleText);

            var timeSpan = DateTime.Now - saveInfo.LastModified;
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
                Foreground = new SolidColorBrush(Colors.Gray),
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
                foreach (var save in saves)
                {
                    try
                    {
                        if (Directory.Exists(save.OriginalPath))
                        {
                            var directoryInfo = new DirectoryInfo(save.OriginalPath);
                            totalSize += CalculateFolderSize(directoryInfo);
                        }
                    }
                    catch
                    {
                        // Skip if folder is inaccessible
                    }
                }
                return totalSize;
            });
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";

            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var unitIndex = 0;
            var size = (double)bytes;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F1} {units[unitIndex]}";
        }

        private int GetManagedSavesCount()
        {
            try
            {
                var managedSavesPath = GetManagedSavesStoragePath();
                if (!Directory.Exists(managedSavesPath))
                {
                    return 0;
                }

                var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
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
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
        }

        private async void CommitAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement commit all changes functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Commit All Changes",
                "Commit all changes feature is coming soon!");
        }

        private async void PullAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pull all updates functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Pull All Updates",
                "Pull all updates feature is coming soon!");
        }

        private async void PushAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement push all changes functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Push All Changes",
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
                    var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    var save = await AnalyzeSaveFolder(folder.Path);
                    if (save != null)
                    {
                        // Add to navigation in MainWindow
                        if (App.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.AddSaveToNavigation(save.Name, save.Path);
                        }

                        // Register this save in our managed saves system
                        await RegisterManagedSave(save);

                        // Refresh the saves list and statistics
                        await LoadManagedSaves();
                    }
                    else
                    {
                        ShowErrorFlyout(sender as FrameworkElement, "Invalid Minecraft Save",
                            "The selected folder doesn't appear to be a valid Minecraft save. A valid save should contain level.dat or level.dat_old.");
                    }
                }
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
            // TODO: Implement Git initialization for selected saves
            ShowSuccessFlyout(sender as FrameworkElement, "Git Initialization",
                "Git initialization feature is coming soon!");
            await Task.CompletedTask;
        }

        private async void SyncAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement sync all functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Sync All",
                "Sync all feature is coming soon!");
            await Task.CompletedTask;
        }

        private async void FetchAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement fetch all functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Fetch All",
                "Fetch all changes feature is coming soon!");
            await Task.CompletedTask;
        }

        private async void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement backup functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Create Backup",
                "Backup feature is coming soon!");
            await Task.CompletedTask;
        }

        // Helper methods
        private async Task RegisterManagedSave(MinecraftSave save)
        {
            try
            {
                var managedSavesPath = GetManagedSavesStoragePath();

                if (!Directory.Exists(managedSavesPath))
                {
                    Directory.CreateDirectory(managedSavesPath);
                }

                var saveId = GenerateSaveId(save.Name);
                var saveInfoPath = Path.Combine(managedSavesPath, $"{saveId}.json");

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

                var json = System.Text.Json.JsonSerializer.Serialize(saveInfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(saveInfoPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register managed save: {ex.Message}");
            }
        }

        private string GenerateSaveId(string saveName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = string.Join("_", saveName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            return $"{safeName}_{timestamp}";
        }

        private Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
        {
            try
            {
                var levelDatPath = Path.Combine(savePath, "level.dat");
                var levelDatOldPath = Path.Combine(savePath, "level.dat_old");

                if (!File.Exists(levelDatPath) && !File.Exists(levelDatOldPath))
                {
                    return Task.FromResult<MinecraftSave?>(null);
                }

                var directoryInfo = new DirectoryInfo(savePath);
                var save = new MinecraftSave
                {
                    Name = directoryInfo.Name,
                    Path = savePath,
                    LastPlayed = directoryInfo.LastWriteTime,
                    WorldSize = CalculateFolderSize(directoryInfo),
                    IsGitInitialized = Directory.Exists(Path.Combine(savePath, "GitMC")),
                    WorldType = "Survival",
                    GameVersion = "1.21"
                };

                save.WorldIcon = save.WorldType.ToLower() switch
                {
                    "creative" => "🎨",
                    "hardcore" => "💀",
                    "spectator" => "👻",
                    "adventure" => "🗺️",
                    _ => "🌍"
                };

                return Task.FromResult<MinecraftSave?>(save);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to analyze save folder: {ex.Message}");
                return Task.FromResult<MinecraftSave?>(null);
            }
        }

        private static long CalculateFolderSize(DirectoryInfo directoryInfo)
        {
            try
            {
                return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        // Flyout helper methods
        private void ShowErrorFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 8),
                            Children =
                            {
                                new FontIcon
                                {
                                    Glyph = "\uE783", // Error icon
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 52, 56)),
                                    Margin = new Thickness(0, 0, 8, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = title,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    FontSize = 16,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            flyout.ShowAt(anchor);
        }

        private void ShowSuccessFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 8),
                            Children =
                            {
                                new FontIcon
                                {
                                    Glyph = "\uE73E", // Checkmark icon
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)),
                                    Margin = new Thickness(0, 0, 8, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = title,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    FontSize = 16,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            flyout.ShowAt(anchor);
        }
    }

    // Data model for managed save information
    public class ManagedSaveInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public DateTime AddedDate { get; set; }
        public DateTime LastModified { get; set; }
        public string GitRepository { get; set; } = "";
        public bool IsGitInitialized { get; set; }
    }
}
