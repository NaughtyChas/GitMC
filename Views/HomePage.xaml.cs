using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GitMC.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private readonly IOnboardingService _onboardingService;
        private readonly IGitService _gitService;
        private readonly IConfigurationService _configurationService;
        private readonly IDataStorageService _dataStorageService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HomePage()
        {
            InitializeComponent();
            _gitService = new GitService();
            _configurationService = new ConfigurationService();
            _dataStorageService = new DataStorageService();
            _onboardingService = new OnboardingService(_gitService, _configurationService);

            DataContext = this;
            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize services
            await _onboardingService.InitializeAsync();

            // Determine which page to show based on managed saves
            if (HasManagedSaves())
            {
                // User has managed saves, show SaveManagementPage
                NavigateToSaveManagement();
            }
            else
            {
                // New user, show OnboardingPage
                NavigateToOnboarding();
            }
        }

        private void NavigateToOnboarding()
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(OnboardingPage));
            }
        }

        private void NavigateToSaveManagement()
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SaveManagementPage));
            }
        }

        private bool HasManagedSaves()
        {
            return GetManagedSavesCount() > 0;
        }

        private int GetManagedSavesCount()
        {
            try
            {
                // Check for managed saves metadata files
                var managedSavesPath = GetManagedSavesStoragePath();
                if (!Directory.Exists(managedSavesPath))
                {
                    return 0;
                }

                // Count JSON metadata files that represent managed saves
                var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
                return jsonFiles.Length;
            }
            catch
            {
                // If there's any error accessing the filesystem, fall back to onboarding check
                var statuses = _onboardingService.StepStatuses;
                if (statuses.Length > 4 && statuses[4] == OnboardingStepStatus.Completed)
                {
                    return 1; // At least one save exists based on onboarding
                }
                return 0;
            }
        }

        private string GetManagedSavesStoragePath()
        {
            return _dataStorageService.GetManagedSavesDirectory();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
