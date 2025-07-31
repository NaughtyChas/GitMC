using System.Collections.ObjectModel;
using GitMC.Models;
using GitMC.Services;
using GitMC.Utils;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitMC.ViewModels
{
    public class HomePageViewModel : BaseViewModel
    {
        private readonly IMinecraftAnalyzerService _analyzerService;
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

        public HomePageViewModel(IMinecraftAnalyzerService analyzerService)
        {
            _analyzerService = analyzerService;
            _welcomeMessage = CommonHelpers.GetWelcomeMessage();
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
                    var save = await _analyzerService.AnalyzeSaveFolder(folder.Path).ConfigureAwait(false);
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
            return await _analyzerService.AnalyzeSaveFolder(savePath).ConfigureAwait(false);
        }

        private async Task LoadSavedDataAsync()
        {
            // In a real implementation, this would load from settings/storage
            // For now, add some sample data
            await Task.Delay(100).ConfigureAwait(false); // Simulate loading
        }

        private static long CalculateFolderSize(DirectoryInfo directoryInfo) => CommonHelpers.CalculateFolderSize(directoryInfo);

        private static string GetWelcomeMessage() => CommonHelpers.GetWelcomeMessage();
    }
}
