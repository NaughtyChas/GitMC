using GitMC.Services;
using GitMC.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private readonly NbtService _nbtService;
        private readonly IGitService _gitService;
        private readonly IOnboardingService _onboardingService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HomePage()
        {
            this.InitializeComponent();
            _nbtService = new NbtService();
            _gitService = new GitService();
            _onboardingService = new OnboardingService(_gitService);
            
            // Subscribe to onboarding changes
            _onboardingService.PropertyChanged += OnboardingService_PropertyChanged;
            
            this.DataContext = this;
            this.Loaded += HomePage_Loaded;
        }

        public IOnboardingService OnboardingService => _onboardingService;

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Refresh onboarding status on page load
            await _onboardingService.RefreshAllSteps();
            UpdateStepVisibility();
            UpdateSystemStatus();
        }

        private void OnboardingService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IOnboardingService.StepStatuses) || 
                e.PropertyName == nameof(IOnboardingService.CurrentStepIndex))
            {
                UpdateStepVisibility();
            }
        }

        private void UpdateStepVisibility()
        {
            var currentStep = _onboardingService.CurrentStepIndex;
            var statuses = _onboardingService.StepStatuses;

            // Update step 1 (Language Configuration)
            UpdateStepDisplay(0, Step1Content, Step1CollapsedContent, currentStep, statuses);
            
            // Update step 2 (Git Installation)  
            UpdateStepDisplay(1, Step2Content, Step2CollapsedContent, currentStep, statuses);
            
            // Update step 3 (Git Configuration)
            UpdateStepDisplay(2, Step3Content, Step3CollapsedContent, currentStep, statuses);
            
            // Update step 4 (Platform Connection)
            UpdateStepDisplay(3, Step4Content, Step4CollapsedContent, currentStep, statuses);
            
            // Update step 5 (Add Save)
            UpdateStepDisplay(4, Step5Content, Step5CollapsedContent, currentStep, statuses);

            // Update step indicators
            UpdateStepIndicator(Step1Circle, Step1CheckIcon, Step1NumberText, 0, statuses);
            UpdateStepIndicator(Step2Circle, Step2CheckIcon, Step2NumberText, 1, statuses);
            UpdateStepIndicator(Step3Circle, Step3CheckIcon, Step3NumberText, 2, statuses);
            UpdateStepIndicator(Step4Circle, Step4CheckIcon, Step4NumberText, 3, statuses);
            UpdateStepIndicator(Step5Circle, Step5CheckIcon, Step5NumberText, 4, statuses);

            // Update step status texts
            UpdateStepStatusTexts();
        }

        private void UpdateStepDisplay(int stepIndex, FrameworkElement? fullContent, FrameworkElement? collapsedContent, int currentStep, OnboardingStepStatus[] statuses)
        {
            if (stepIndex >= statuses.Length || fullContent == null || collapsedContent == null) return;

            bool isCurrentStep = stepIndex == currentStep && statuses[stepIndex] == OnboardingStepStatus.Current;
            
            fullContent.Visibility = isCurrentStep ? Visibility.Visible : Visibility.Collapsed;
            collapsedContent.Visibility = isCurrentStep ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateStepIndicator(Border? circleBorder, FontIcon? checkIcon, TextBlock? numberText, int stepIndex, OnboardingStepStatus[] statuses)
        {
            if (stepIndex >= statuses.Length || circleBorder == null) return;

            var status = statuses[stepIndex];
            
            switch (status)
            {
                case OnboardingStepStatus.Completed:
                    circleBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16)); // Green
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Visible;
                    if (numberText != null) numberText.Visibility = Visibility.Collapsed;
                    break;
                    
                case OnboardingStepStatus.Current:
                    circleBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212)); // Blue
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                    if (numberText != null) numberText.Visibility = Visibility.Visible;
                    break;
                    
                case OnboardingStepStatus.NotStarted:
                default:
                    circleBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 209, 209, 209)); // Grey
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                    if (numberText != null) numberText.Visibility = Visibility.Visible;
                    break;
            }
        }

        private async void UpdateStepStatusTexts()
        {
            try
            {
                var statuses = _onboardingService.StepStatuses;

                // Step 2: Git Installation
                if (statuses.Length > 1)
                {
                    bool gitInstalled = await _gitService.IsInstalledAsync();
                    if (gitInstalled && (statuses[1] == OnboardingStepStatus.Completed || statuses[1] == OnboardingStepStatus.Current))
                    {
                        var gitVersion = await _gitService.GetVersionAsync();
                        GitFoundText.Text = $"✓ Git v{gitVersion} has been installed and ready to use.";
                        GitFoundText.Visibility = Visibility.Visible;
                        GitNotFoundPanel.Visibility = Visibility.Collapsed;
                    }
                    else if (statuses[1] == OnboardingStepStatus.Current)
                    {
                        GitFoundText.Visibility = Visibility.Collapsed;
                        GitNotFoundPanel.Visibility = Visibility.Visible;
                    }
                }

                // Step 3: Git Configuration  
                if (statuses.Length > 2)
                {
                    var (userName, userEmail) = await _gitService.GetIdentityAsync();
                    if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail) && 
                        statuses[2] == OnboardingStepStatus.Completed)
                    {
                        // Find Git config status text element and update it
                        var gitConfigText = FindName("GitConfigCompletedText") as TextBlock;
                        if (gitConfigText != null)
                        {
                            gitConfigText.Text = $"✓ Configured as {userName} ({userEmail})";
                            gitConfigText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16)); // Green
                            gitConfigText.Visibility = Visibility.Visible;
                        }
                    }
                }

                // Step 4: Repository Setup
                if (statuses.Length > 3 && statuses[3] == OnboardingStepStatus.Completed)
                {
                    var repoStatusText = FindName("RepoSetupCompletedText") as TextBlock;
                    if (repoStatusText != null)
                    {
                        repoStatusText.Text = "✓ Repository configured successfully";
                        repoStatusText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16)); // Green
                        repoStatusText.Visibility = Visibility.Visible;
                    }
                }

                // Step 5: Usage Mode
                if (statuses.Length > 4 && statuses[4] == OnboardingStepStatus.Completed)
                {
                    var usageModeText = FindName("UsageModeCompletedText") as TextBlock;
                    if (usageModeText != null)
                    {
                        usageModeText.Text = "✓ Usage mode configured";
                        usageModeText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16)); // Green
                        usageModeText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch { }
        }

        private async void LanguageSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to language settings
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
            
            // Mark language as configured
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["LanguageConfigured"] = true;
                await _onboardingService.CompleteStep(0);
            }
            catch { }
        }

        private async void SkipLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip language configuration and move to next step
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["LanguageConfigured"] = true;
                await _onboardingService.CompleteStep(0);
            }
            catch { }
        }

        private async void UseLocallyButton_Click(object sender, RoutedEventArgs e)
        {
            // Configure for local use only
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                localSettings.Values["PlatformConfigured"] = true;
                await _onboardingService.CompleteStep(3);
                
                // Show confirmation
                var dialog = new ContentDialog
                {
                    Title = "Local Mode Activated",
                    Content = "GitMC will now operate in local mode. Your saves will be managed locally with Git version control.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }

        private async void GitConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Git configuration settings
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SettingsPage));
            }
            
            // Check if Git is configured after navigation
            await Task.Delay(1000); // Small delay to allow settings page to potentially configure Git
            await _onboardingService.RefreshAllSteps();
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
                        }
                        
                        // Mark save as added
                        try
                        {
                            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                            localSettings.Values["SaveAdded"] = true;
                            await _onboardingService.CompleteStep(4);
                        }
                        catch { }
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

        // Update the real-time status checking
        private async void UpdateSystemStatus()
        {
            try
            {
                // Check Git installation
                var gitInstalled = await _gitService.IsInstalledAsync();
                if (gitInstalled)
                {
                    var gitVersion = await _gitService.GetVersionAsync();
                    GitStatusIcon.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16));
                    GitStatusText.Text = $"Git v{gitVersion} installed and ready";
                }
                else
                {
                    GitStatusIcon.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 140, 0));
                    GitStatusText.Text = "Git is not installed. Please install Git to continue.";
                }

                // Check Git identity
                var (userName, userEmail) = await _gitService.GetIdentityAsync();
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail))
                {
                    GitIdentityStatusIcon.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 124, 16));
                    GitIdentityStatusText.Text = $"Configured as {userName} ({userEmail})";
                }
                else
                {
                    GitIdentityStatusIcon.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 140, 0));
                    GitIdentityStatusText.Text = "Git identity not configured. Set your name and email.";
                }

                // Update overall progress
                var completedSteps = _onboardingService.StepStatuses.Count(s => s == OnboardingStepStatus.Completed);
                var totalSteps = _onboardingService.StepStatuses.Length;
                var progressPercentage = totalSteps > 0 ? (completedSteps * 100.0 / totalSteps) : 0;
                
                OverallProgressBar.Value = progressPercentage;
                ProgressText.Text = $"{completedSteps} of {totalSteps} steps completed";
            }
            catch { }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
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
