using System.ComponentModel;
using Windows.Storage;

namespace GitMC.Services
{
    public class OnboardingService : IOnboardingService
    {
        private readonly IGitService _gitService;
        private int _currentStepIndex;
        private OnboardingStepStatus[] _stepStatuses = Array.Empty<OnboardingStepStatus>();
        private OnboardingStep[] _steps = Array.Empty<OnboardingStep>();
        
        // Cache for ApplicationData values to avoid thread issues
        private bool? _isLanguageConfigured;
        private bool? _isPlatformConfigured;
        private bool? _isSaveAdded;

        public event PropertyChangedEventHandler? PropertyChanged;

        public OnboardingService(IGitService gitService)
        {
            _gitService = gitService;
            InitializeSteps();
            // Don't call RefreshAllSteps in constructor to avoid thread issues
            // It will be called from the UI thread in HomePage_Loaded
        }

        // Method to safely update cached values from UI thread
        public void RefreshApplicationDataCache()
        {
            try
            {
                // Small delay to ensure app is fully initialized
                Task.Delay(100).Wait();
                
                var localSettings = ApplicationData.Current.LocalSettings;
                _isLanguageConfigured = localSettings.Values.ContainsKey("LanguageConfigured");
                _isPlatformConfigured = localSettings.Values.ContainsKey("PlatformConfigured");
                _isSaveAdded = localSettings.Values.ContainsKey("SaveAdded");
            }
            catch
            {
                // If we can't access ApplicationData, use default values
                _isLanguageConfigured ??= false;
                _isPlatformConfigured ??= false;
                _isSaveAdded ??= false;
            }
        }

        // Safe method to set ApplicationData values and update cache
        public void SetConfigurationValue(string key, bool value)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values[key] = value;
                
                // Update corresponding cache
                switch (key)
                {
                    case "LanguageConfigured":
                        _isLanguageConfigured = value;
                        break;
                    case "PlatformConfigured":
                        _isPlatformConfigured = value;
                        break;
                    case "SaveAdded":
                        _isSaveAdded = value;
                        break;
                }
            }
            catch
            {
                // If we can't access ApplicationData, just update cache
                switch (key)
                {
                    case "LanguageConfigured":
                        _isLanguageConfigured = value;
                        break;
                    case "PlatformConfigured":
                        _isPlatformConfigured = value;
                        break;
                    case "SaveAdded":
                        _isSaveAdded = value;
                        break;
                }
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
                    Title = "Install Git",
                    ShortDescription = "Git version control system",
                    FullDescription = "Git is required for version control functionality.",
                    StatusChecker = CheckGitInstallation
                },
                new OnboardingStep
                {
                    Title = "Configure Git",
                    ShortDescription = "Mark your changes in the Git system",
                    FullDescription = "In order to make your save works with version control, you have to provide your credentials. If you wish to connect to any code hosting platform, fill in your platform email and username here.",
                    StatusChecker = CheckGitConfiguration
                },
                new OnboardingStep
                {
                    Title = "Connect to Code-Hosting Platform",
                    ShortDescription = "Optional cloud sync setup",
                    FullDescription = "Save and sync your save on cloud, you can choose which platform to use, or use GitMC locally.",
                    StatusChecker = CheckPlatformConnection
                },
                new OnboardingStep
                {
                    Title = "Add Your Save",
                    ShortDescription = "Start managing your worlds",
                    FullDescription = "Now you can manage your save with versioning control! Add your first save or add a Minecraft version folder.",
                    StatusChecker = CheckSaveAdded
                }
            };

            _stepStatuses = new OnboardingStepStatus[_steps.Length];
            for (int i = 0; i < _stepStatuses.Length; i++)
            {
                _stepStatuses[i] = i == 0 ? OnboardingStepStatus.Current : OnboardingStepStatus.NotStarted;
            }
        }

        public bool IsOnboardingComplete => 
            _stepStatuses.Length > 0 && 
            Array.TrueForAll(_stepStatuses, status => status == OnboardingStepStatus.Completed);

        public int CurrentStepIndex => _currentStepIndex;

        public OnboardingStepStatus[] StepStatuses => _stepStatuses;

        public bool IsFirstLaunch
        {
            get
            {
                try
                {
                    var localSettings = ApplicationData.Current.LocalSettings;
                    return !localSettings.Values.ContainsKey("FirstLaunchComplete");
                }
                catch (InvalidOperationException)
                {
                    // If we can't access ApplicationData (wrong thread), assume first launch
                    return true;
                }
                catch
                {
                    return true;
                }
            }
        }

        public void MarkFirstLaunchComplete()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["FirstLaunchComplete"] = true;
            }
            catch (InvalidOperationException)
            {
                // If we can't access ApplicationData (wrong thread), ignore
                System.Diagnostics.Debug.WriteLine("MarkFirstLaunchComplete: Cannot access ApplicationData from this thread");
            }
            catch { }
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
            if (stepIndex < 0 || stepIndex >= _stepStatuses.Length)
                return;

            _stepStatuses[stepIndex] = OnboardingStepStatus.Completed;
            
            // Refresh all steps to check actual conditions and update current step
            await RefreshAllSteps();
        }

        public async Task MoveToNextStep()
        {
            // Find next incomplete step
            for (int i = _currentStepIndex + 1; i < _stepStatuses.Length; i++)
            {
                if (_stepStatuses[i] != OnboardingStepStatus.Completed)
                {
                    // Mark previous step as completed if it's verified
                    if (await CheckStepStatus(_currentStepIndex))
                    {
                        _stepStatuses[_currentStepIndex] = OnboardingStepStatus.Completed;
                    }

                    _currentStepIndex = i;
                    _stepStatuses[i] = OnboardingStepStatus.Current;
                    break;
                }
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
                
                if (isComplete && _stepStatuses[i] != OnboardingStepStatus.Completed)
                {
                    _stepStatuses[i] = OnboardingStepStatus.Completed;
                    hasChanges = true;
                }
            }

            // Second pass: Find the current step (first non-completed step)
            int newCurrentStep = -1;
            for (int i = 0; i < _stepStatuses.Length; i++)
            {
                if (_stepStatuses[i] != OnboardingStepStatus.Completed)
                {
                    newCurrentStep = i;
                    break;
                }
            }

            // Update current step if needed
            if (newCurrentStep != -1)
            {
                if (_currentStepIndex != newCurrentStep)
                {
                    // Clear old current step
                    if (_currentStepIndex < _stepStatuses.Length && _stepStatuses[_currentStepIndex] == OnboardingStepStatus.Current)
                    {
                        _stepStatuses[_currentStepIndex] = OnboardingStepStatus.NotStarted;
                    }
                    
                    _currentStepIndex = newCurrentStep;
                    hasChanges = true;
                }
                
                if (_stepStatuses[newCurrentStep] != OnboardingStepStatus.Current)
                {
                    _stepStatuses[newCurrentStep] = OnboardingStepStatus.Current;
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

        // Step-specific checkers
        private Task<bool> CheckLanguageConfiguration()
        {
            // Use cached value to avoid ApplicationData access issues
            return Task.FromResult(_isLanguageConfigured ?? false);
        }

        private async Task<bool> CheckGitInstallation()
        {
            return await _gitService.IsInstalledAsync();
        }

        private async Task<bool> CheckGitConfiguration()
        {
            try
            {
                var (userName, userEmail) = await _gitService.GetIdentityAsync();
                return !string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail);
            }
            catch
            {
                return false;
            }
        }

        private Task<bool> CheckPlatformConnection()
        {
            // Use cached value to avoid ApplicationData access issues
            return Task.FromResult(_isPlatformConfigured ?? false);
        }

        private Task<bool> CheckSaveAdded()
        {
            // Use cached value to avoid ApplicationData access issues
            return Task.FromResult(_isSaveAdded ?? false);
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
