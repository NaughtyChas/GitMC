using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using GitMC.Models;
using GitMC.Services;
using WinRT.Interop;

namespace GitMC.ViewModels
{
    public class HomePageViewModel : INotifyPropertyChanged
    {
        private readonly INbtService _nbtService;
        private bool _isLoading;
        private string _welcomeMessage;
        private MinecraftSave? _selectedSave;

        public ObservableCollection<MinecraftSave> RecentSaves { get; }
        public ObservableCollection<MinecraftSave> ManagedSaves { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set => SetProperty(ref _welcomeMessage, value);
        }

        public MinecraftSave? SelectedSave
        {
            get => _selectedSave;
            set => SetProperty(ref _selectedSave, value);
        }

        public bool HasSaves => ManagedSaves.Count > 0;
        public bool HasNoSaves => !HasSaves;

        public HomePageViewModel(INbtService nbtService)
        {
            _nbtService = nbtService;
            _welcomeMessage = GetWelcomeMessage();
            RecentSaves = new ObservableCollection<MinecraftSave>();
            ManagedSaves = new ObservableCollection<MinecraftSave>();

            // Load saved data
            _ = LoadSavedDataAsync();
        }

        public async Task AddSaveAsync()
        {
            try
            {
                IsLoading = true;

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
                        // Add to both collections
                        ManagedSaves.Add(save);
                        if (RecentSaves.Count >= 5)
                        {
                            RecentSaves.RemoveAt(4);
                        }
                        RecentSaves.Insert(0, save);

                        // Add to navigation
                        if (App.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.AddSaveToNavigation(save.Name, save.Path);
                        }

                        // Update properties
                        OnPropertyChanged(nameof(HasSaves));
                        OnPropertyChanged(nameof(HasNoSaves));

                        // Save to storage access list for future access
                        StorageApplicationPermissions.FutureAccessList.Add(folder);
                    }
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
        {
            try
            {
                // Validate it's a Minecraft save
                var levelDatPath = Path.Combine(savePath, "level.dat");
                var levelDatOldPath = Path.Combine(savePath, "level.dat_old");

                if (!File.Exists(levelDatPath) && !File.Exists(levelDatOldPath))
                {
                    return null;
                }

                var directoryInfo = new DirectoryInfo(savePath);
                var save = new MinecraftSave
                {
                    Name = directoryInfo.Name,
                    Path = savePath,
                    LastPlayed = directoryInfo.LastWriteTime,
                    WorldSize = CalculateFolderSize(directoryInfo),
                    IsGitInitialized = Directory.Exists(Path.Combine(savePath, "GitMC")),
                    WorldType = "Survival" // Default, could be enhanced to read from level.dat
                };

                // Analyze NBT data if available
                if (File.Exists(levelDatPath))
                {
                    try
                    {
                        var nbtInfo = await _nbtService.GetNbtFileInfoAsync(levelDatPath);
                        // Parse game version and world type from NBT info if possible
                        save.GameVersion = ExtractVersionFromNbtInfo(nbtInfo);
                        save.WorldType = ExtractWorldTypeFromNbtInfo(nbtInfo);
                    }
                    catch
                    {
                        // Continue with defaults if NBT reading fails
                    }
                }

                // Check Git status
                if (save.IsGitInitialized)
                {
                    save.GitStatus = "Initialized";
                    // Could add logic to check for pending changes
                }

                // Set appropriate world icon based on world type
                save.WorldIcon = save.WorldType.ToLower() switch
                {
                    "creative" => "🎨",
                    "hardcore" => "💀",
                    "spectator" => "👻",
                    "adventure" => "🗺️",
                    _ => "🌍"
                };

                return save;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task LoadSavedDataAsync()
        {
            // In a real implementation, this would load from settings/storage
            // For now, add some sample data
            await Task.Delay(100); // Simulate loading
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

        private static string ExtractVersionFromNbtInfo(string nbtInfo)
        {
            // Parse version from NBT info - this is a simplified implementation
            if (nbtInfo.Contains("Version"))
            {
                // Extract version info - would need proper NBT parsing
                return "1.21"; // Default for now
            }
            return "Unknown";
        }

        private static string ExtractWorldTypeFromNbtInfo(string nbtInfo)
        {
            // Parse world type from NBT info - simplified implementation
            if (nbtInfo.Contains("creative") || nbtInfo.Contains("Creative"))
                return "Creative";
            if (nbtInfo.Contains("hardcore") || nbtInfo.Contains("Hardcore"))
                return "Hardcore";
            return "Survival";
        }

        private static string GetWelcomeMessage()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 12 => "Good morning, Crafter! ☀️",
                < 18 => "Good afternoon, Miner! ⛏️",
                _ => "Good evening, Builder! 🌙"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
