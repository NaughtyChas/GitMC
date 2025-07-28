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

        public event PropertyChangedEventHandler? PropertyChanged;

        public OnboardingService(IGitService gitService)
        {
            _gitService = gitService;
            InitializeSteps();
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
            
            // Move to next step if current
            if (stepIndex == _currentStepIndex)
            {
                await MoveToNextStep();
            }

            NotifyPropertyChanged(nameof(StepStatuses));
            NotifyPropertyChanged(nameof(IsOnboardingComplete));
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

            for (int i = 0; i < _steps.Length; i++)
            {
                bool isComplete = await CheckStepStatus(i);
                var expectedStatus = isComplete ? OnboardingStepStatus.Completed : 
                                   i == _currentStepIndex ? OnboardingStepStatus.Current : 
                                   OnboardingStepStatus.NotStarted;

                if (_stepStatuses[i] != expectedStatus)
                {
                    _stepStatuses[i] = expectedStatus;
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                NotifyPropertyChanged(nameof(StepStatuses));
                NotifyPropertyChanged(nameof(IsOnboardingComplete));
            }
        }

        // Step-specific checkers
        private Task<bool> CheckLanguageConfiguration()
        {
            // Language is considered configured if it's been explicitly set
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                return Task.FromResult(localSettings.Values.ContainsKey("LanguageConfigured"));
            }
            catch
            {
                return Task.FromResult(false);
            }
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
            // This is optional, so we'll consider it complete if user has chosen "local mode"
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                return Task.FromResult(localSettings.Values.ContainsKey("PlatformConfigured"));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        private Task<bool> CheckSaveAdded()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                return Task.FromResult(localSettings.Values.ContainsKey("SaveAdded"));
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
