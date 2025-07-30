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
using WinRT.Interop;

namespace GitMC.Views
{
    public sealed partial class SaveManagementPage : Page, INotifyPropertyChanged
    {
        private readonly NbtService _nbtService;
        private readonly IGitService _gitService;
        private readonly IConfigurationService _configurationService;
        private readonly IOnboardingService _onboardingService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SaveManagementPage()
        {
            InitializeComponent();
            _nbtService = new NbtService();
            _gitService = new GitService();
            _configurationService = new ConfigurationService();
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
            var savesContainer = FindName("SavesContainer") as StackPanel;
            if (savesContainer == null) return;

            // Clear existing items (except the sample ones for now)
            // In production, you'd replace the sample cards with real data

            // For now, we'll just update the statistics
            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            // Update the statistics in the status bar
            var totalSavesCount = FindName("TotalSavesCount") as TextBlock;
            var gitInitializedCount = FindName("GitInitializedCount") as TextBlock;
            var totalSizeText = FindName("TotalSizeText") as TextBlock;

            if (totalSavesCount != null)
            {
                totalSavesCount.Text = GetManagedSavesCount().ToString();
            }

            // These would be calculated from actual data
            if (gitInitializedCount != null)
            {
                gitInitializedCount.Text = "0"; // TODO: Calculate from actual saves
            }

            if (totalSizeText != null)
            {
                totalSizeText.Text = "0 MB"; // TODO: Calculate from actual saves
            }
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
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "GitMC", "ManagedSaves");
        }

        // Event handlers
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
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
        }

        private async void SyncAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement sync all functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Sync All",
                "Sync all feature is coming soon!");
        }

        private async void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement backup functionality
            ShowSuccessFlyout(sender as FrameworkElement, "Create Backup",
                "Backup feature is coming soon!");
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
