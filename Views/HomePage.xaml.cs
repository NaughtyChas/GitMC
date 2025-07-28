using GitMC.Services;
using GitMC.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly NbtService _nbtService;

        public HomePage()
        {
            this.InitializeComponent();
            _nbtService = new NbtService();
        }

        private async void AddSaveButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingProgressRing.IsIndeterminate = true;
            
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
                            // Navigate to save dashboard would go here in future
                        }
                    }
                    else
                    {
                        // Show error message
                        var dialog = new ContentDialog
                        {
                            Title = "Invalid Minecraft Save",
                            Content = "The selected folder doesn't appear to be a valid Minecraft save. A valid save should contain level.dat or level.dat_old.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await dialog.ShowAsync();
                    }
                }
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                LoadingProgressRing.IsIndeterminate = false;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
        }

        private void LanguageSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to language settings
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
        }

        private async void DownloadGitButton_Click(object sender, RoutedEventArgs e)
        {
            // Open Git download page
            var uri = new Uri("https://git-scm.com/download/windows");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        private void GitConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Git configuration settings
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
        }

        private async void ConnectPlatformButton_Click(object sender, RoutedEventArgs e)
        {
            // Show platform connection dialog or navigate to connection page
            var dialog = new ContentDialog
            {
                Title = "Connect to Platform",
                Content = "Platform connection functionality will be implemented in future updates.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void UseLocallyButton_Click(object sender, RoutedEventArgs e)
        {
            // Configure for local use only
            // This would typically hide cloud-related features and configure local Git
            // For now, just show a message
            var dialog = new ContentDialog
            {
                Title = "Local Mode Activated",
                Content = "GitMC will now operate in local mode. Your saves will be managed locally with Git version control.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private async void AddVersionFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Similar to AddSaveButton_Click but specifically for version folders
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingProgressRing.IsIndeterminate = true;
            
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
                    // For version folders, we might have different validation logic
                    var save = await AnalyzeSaveFolder(folder.Path);
                    if (save != null)
                    {
                        if (App.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.AddSaveToNavigation(save.Name, save.Path);
                        }
                    }
                    else
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Invalid Folder",
                            Content = "The selected folder doesn't appear to contain valid Minecraft data.",
                            CloseButtonText = "OK",
                            XamlRoot = this.XamlRoot
                        };
                        await dialog.ShowAsync();
                    }
                }
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                LoadingProgressRing.IsIndeterminate = false;
            }
        }

        private Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
        {
            try
            {
                // Validate it's a Minecraft save
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
                    WorldType = "Survival", // Default
                    GameVersion = "1.21" // Default
                };

                // Set appropriate world icon based on world type
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
            catch
            {
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
    }
}
