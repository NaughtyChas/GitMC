using System.ComponentModel;
using System.Diagnostics;

namespace GitMC.Services;

public class OnboardingService : IOnboardingService
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private OnboardingStep[] _steps = Array.Empty<OnboardingStep>();

    public OnboardingService(IGitService gitService, IConfigurationService configurationService)
    {
        _gitService = gitService;
        _configurationService = configurationService;
        _dataStorageService = new DataStorageService();
        InitializeSteps();

        // Subscribe to configuration changes to update steps
        _configurationService.ConfigurationChanged += OnConfigurationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Updated method for new configuration system
    public async Task InitializeAsync()
    {
        await _configurationService.LoadAsync();
        await RefreshAllSteps();
    }

    // Safe method to set configuration values
    public async Task SetConfigurationValueAsync(string key, bool value)
    {
        switch (key)
        {
            case "LanguageConfigured":
                _configurationService.IsLanguageConfigured = value;
                break;
            case "GitSystemConfigured":
                _configurationService.IsGitSystemConfigured = value;
                break;
            case "GitIdentityConfigured":
                _configurationService.IsGitIdentityConfigured = value;
                break;
            case "PlatformConfigured":
                _configurationService.IsPlatformConfigured = value;
                break;
            case "SaveAdded":
                _configurationService.IsSaveAdded = value;
                break;
        }

        // Ensure configuration is immediately saved to file
        await _configurationService.SaveAsync();

        // Refresh steps after configuration change
        await RefreshAllSteps();
    }

    // Legacy method kept for compatibility - now just delegates
    public async Task RefreshApplicationDataCacheAsync()
    {
        // No longer needed with file-based configuration
        await RefreshAllSteps();
    }

    // Synchronous version for backward compatibility
    public void SetConfigurationValue(string key, bool value)
    {
        _ = SetConfigurationValueAsync(key, value);
    }

    // Synchronous version for backward compatibility
    public void RefreshApplicationDataCache()
    {
        _ = RefreshApplicationDataCacheAsync();
    }

    public bool IsOnboardingComplete =>
        StepStatuses.Length > 0 &&
        Array.TrueForAll(StepStatuses, status => status == OnboardingStepStatus.Completed);

    public int CurrentStepIndex { get; private set; }

    public OnboardingStepStatus[] StepStatuses { get; private set; } = Array.Empty<OnboardingStepStatus>();

    public bool IsFirstLaunch => !_configurationService.IsFirstLaunchComplete;

    public void MarkFirstLaunchComplete()
    {
        _configurationService.IsFirstLaunchComplete = true;
    }

    public async Task<bool> CheckStepStatus(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= _steps.Length)
            return false;

        try
        {
            return await _steps[stepIndex].StatusChecker();
        }
        catch
        {
            return false;
        }
    }

    public async Task CompleteStep(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= StepStatuses.Length)
            return;

        StepStatuses[stepIndex] = OnboardingStepStatus.Completed;

        // Refresh all steps to check actual conditions and update current step
        await RefreshAllSteps();
    }

    public async Task MoveToNextStep()
    {
        // Find next incomplete step
        for (int i = CurrentStepIndex + 1; i < StepStatuses.Length; i++)
            if (StepStatuses[i] != OnboardingStepStatus.Completed)
            {
                // Mark previous step as completed if it's verified
                if (await CheckStepStatus(CurrentStepIndex))
                    StepStatuses[CurrentStepIndex] = OnboardingStepStatus.Completed;

                CurrentStepIndex = i;
                StepStatuses[i] = OnboardingStepStatus.Current;
                break;
            }

        NotifyPropertyChanged(nameof(CurrentStepIndex));
        NotifyPropertyChanged(nameof(StepStatuses));
        NotifyPropertyChanged(nameof(IsOnboardingComplete));
    }

    public async Task RefreshAllSteps()
    {
        bool hasChanges = false;

        // First pass: Update completion status based on actual conditions
        for (int i = 0; i < _steps.Length; i++)
        {
            bool isComplete = await CheckStepStatus(i);

            if (isComplete && StepStatuses[i] != OnboardingStepStatus.Completed)
            {
                StepStatuses[i] = OnboardingStepStatus.Completed;
                hasChanges = true;
            }
        }

        // Second pass: Find the current step (first non-completed step)
        int newCurrentStep = -1;
        for (int i = 0; i < StepStatuses.Length; i++)
            if (StepStatuses[i] != OnboardingStepStatus.Completed)
            {
                newCurrentStep = i;
                break;
            }

        // Update current step if needed
        if (newCurrentStep != -1)
        {
            if (CurrentStepIndex != newCurrentStep)
            {
                // Clear old current step
                if (CurrentStepIndex < StepStatuses.Length &&
                    StepStatuses[CurrentStepIndex] == OnboardingStepStatus.Current)
                    StepStatuses[CurrentStepIndex] = OnboardingStepStatus.NotStarted;

                CurrentStepIndex = newCurrentStep;
                hasChanges = true;
            }

            if (StepStatuses[newCurrentStep] != OnboardingStepStatus.Current)
            {
                StepStatuses[newCurrentStep] = OnboardingStepStatus.Current;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            NotifyPropertyChanged(nameof(CurrentStepIndex));
            NotifyPropertyChanged(nameof(StepStatuses));
            NotifyPropertyChanged(nameof(IsOnboardingComplete));
        }
    }

    private async void OnConfigurationChanged(object? sender, string key)
    {
        try
        {
            // When configuration changes, refresh the relevant step
            await RefreshAllSteps();
        }
        catch (Exception ex)
        {
            // Log the exception or handle it gracefully to prevent app crash
            Debug.WriteLine($"Error in OnConfigurationChanged: {ex.Message}");
        }
    }

    private void InitializeSteps()
    {
        _steps = new[]
        {
            new OnboardingStep
            {
                Title = "Configure GitMC",
                ShortDescription = "Set your preferred language",
                FullDescription = "Not your language? Decide GitMC display language at settings.",
                StatusChecker = CheckLanguageConfiguration
            },
            new OnboardingStep
            {
                Title = "Git System Setup",
                ShortDescription = "Choose your Git implementation",
                FullDescription = "GitMC includes built-in Git support which works without installing Git. You can download and install system Git for enhanced functionality and ecosystem compatibility, or skip to use the built-in system.",
                StatusChecker = CheckGitSystemSetup
            },
            new OnboardingStep
            {
                Title = "Configure Git Identity",
                ShortDescription = "Set your author information",
                FullDescription =
                    "Configure your name and email for Git commits. This step requires manual configuration regardless of any existing system Git settings.",
                StatusChecker = CheckGitIdentityConfiguration
            },
            new OnboardingStep
            {
                Title = "Connect to Code-Hosting Platform",
                ShortDescription = "Optional cloud sync setup",
                FullDescription =
                    "Save and sync your save on cloud, you can choose which platform to use, or use GitMC locally.",
                StatusChecker = CheckPlatformConnection
            },
            new OnboardingStep
            {
                Title = "Add Your Save",
                ShortDescription = "Start managing your worlds",
                FullDescription =
                    "Now you can manage your save with versioning control! Add your first save or add a Minecraft version folder.",
                StatusChecker = CheckSaveAdded
            }
        };

        StepStatuses = new OnboardingStepStatus[_steps.Length];
        for (int i = 0; i < StepStatuses.Length; i++)
            StepStatuses[i] = i == 0 ? OnboardingStepStatus.Current : OnboardingStepStatus.NotStarted;
    }

    // Step-specific checkers
    private Task<bool> CheckLanguageConfiguration()
    {
        return Task.FromResult(_configurationService.IsLanguageConfigured);
    }

    private async Task<bool> CheckGitSystemSetup()
    {
        // Check if user has already made a choice (download Git or use built-in)
        if (_configurationService.IsGitSystemConfigured)
        {
            return true;
        }

        // Auto-detect if system Git is installed
        bool isGitInstalled = await _gitService.IsInstalledAsync();
        if (isGitInstalled)
        {
            // Auto-complete step if Git is detected
            _configurationService.IsGitSystemConfigured = true;
            await _configurationService.SaveAsync();
            return true;
        }

        // Otherwise, step requires manual completion (download or use built-in)
        return false;
    }

    private async Task<bool> CheckGitInstallation()
    {
        // Since we use LibGit2Sharp, Git is always "available"
        // But we still check for system Git for enhanced functionality
        return await _gitService.IsInstalledAsync();
    }

    private Task<bool> CheckGitIdentityConfiguration()
    {
        // This step now requires manual configuration regardless of system Git identity
        // We check if the user has explicitly configured identity in GitMC
        return Task.FromResult(_configurationService.IsGitIdentityConfigured);
    }

    private async Task<bool> CheckGitConfiguration()
    {
        try
        {
            return await _gitService.HasGitIdentityAsync();
        }
        catch
        {
            return false;
        }
    }

    private Task<bool> CheckPlatformConnection()
    {
        return Task.FromResult(_configurationService.IsPlatformConfigured);
    }

    private Task<bool> CheckSaveAdded()
    {
        // Check actual managed saves existence instead of just config flag
        return Task.FromResult(HasActualManagedSaves());
    }

    private bool HasActualManagedSaves()
    {
        try
        {
            string managedSavesPath = GetManagedSavesStoragePath();
            if (!Directory.Exists(managedSavesPath)) return false;

            string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            return jsonFiles.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private string GetManagedSavesStoragePath()
    {
        return _dataStorageService.GetManagedSavesDirectory();
    }

    private void NotifyPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
