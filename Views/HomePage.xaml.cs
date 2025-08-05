using System.ComponentModel;
using System.Diagnostics;
using GitMC.Extensions;
using GitMC.Services;

namespace GitMC.Views;

public sealed partial class HomePage : Page, INotifyPropertyChanged
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private readonly IOnboardingService _onboardingService;

    public HomePage()
    {
        InitializeComponent();

        // Use ServiceFactory for consistent service instances
        var services = ServiceFactory.Services;
        _configurationService = services.Configuration;
        _gitService = services.Git;
        _dataStorageService = services.DataStorage;
        _onboardingService = services.Onboarding;

        DataContext = this;
        Loaded += HomePage_Loaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize services
            await _onboardingService.InitializeAsync().ConfigureAwait(false);

            // Determine which page to show based on managed saves
            if (HasManagedSaves())
                // User has managed saves, show SaveManagementPage
                NavigateToSaveManagement();
            else
                // New user, show OnboardingPage
                NavigateToOnboarding();
        }
        catch (Exception ex)
        {
            // Log the exception to prevent app crash
            Debug.WriteLine($"Error in HomePage_Loaded: {ex.Message}");
        }
    }

    private void NavigateToOnboarding()
    {
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(OnboardingPage));
    }

    private void NavigateToSaveManagement()
    {
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(SaveManagementPage));
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
            string managedSavesPath = GetManagedSavesStoragePath();
            if (!Directory.Exists(managedSavesPath)) return 0;

            // Count JSON metadata files that represent managed saves
            string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            return jsonFiles.Length;
        }
        catch
        {
            // If there's any error accessing the filesystem, fall back to onboarding check
            OnboardingStepStatus[] statuses = _onboardingService.StepStatuses;
            if (statuses.Length > 4 &&
                statuses[4] == OnboardingStepStatus.Completed) return 1; // At least one save exists based on onboarding
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
