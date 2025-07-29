using System.ComponentModel;
using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using GitMC.Models;
using GitMC.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private readonly NbtService _nbtService;
        private readonly IGitService _gitService;
        private readonly IConfigurationService _configurationService;
        private readonly IOnboardingService _onboardingService;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HomePage()
        {
            InitializeComponent();
            _nbtService = new NbtService();
            _gitService = new GitService();
            _configurationService = new ConfigurationService();
            _onboardingService = new OnboardingService(_gitService, _configurationService);
            
            // Subscribe to onboarding changes
            _onboardingService.PropertyChanged += OnboardingService_PropertyChanged;
            
            DataContext = this;
            Loaded += HomePage_Loaded;
        }

        public IOnboardingService OnboardingService => _onboardingService;

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize the configuration service and onboarding
            await _onboardingService.InitializeAsync();
            try
            {
                _onboardingService.RefreshApplicationDataCache();
            }
            catch
            {
                // If cache refresh fails, continue anyway
            }
            
            // Then refresh onboarding status on page load
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
                UpdateSystemStatus();
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

            // Check if all previous steps are completed
            bool canBeActive = stepIndex == 0 || statuses.Take(stepIndex).All(s => s == OnboardingStepStatus.Completed);
            bool isCurrentStep = stepIndex == currentStep && statuses[stepIndex] == OnboardingStepStatus.Current && canBeActive;
            
            fullContent.Visibility = isCurrentStep ? Visibility.Visible : Visibility.Collapsed;
            collapsedContent.Visibility = isCurrentStep ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateStepIndicator(Border? circleBorder, FontIcon? checkIcon, TextBlock? numberText, int stepIndex, OnboardingStepStatus[] statuses)
        {
            if (stepIndex >= statuses.Length || circleBorder == null) return;

            var status = statuses[stepIndex];
            var currentStep = _onboardingService.CurrentStepIndex;
            
            // Check if all previous steps are completed
            bool canBeActive = stepIndex == 0 || (stepIndex <= currentStep && 
                statuses.Take(stepIndex).All(s => s == OnboardingStepStatus.Completed));
            
            switch (status)
            {
                case OnboardingStepStatus.Completed:
                    circleBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)); // Green
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Visible;
                    if (numberText != null) numberText.Visibility = Visibility.Collapsed;
                    break;
                    
                case OnboardingStepStatus.Current:
                    if (canBeActive)
                    {
                        circleBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 212)); // Blue
                        if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                        if (numberText != null) numberText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Show as not started if previous steps aren't completed
                        circleBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 209, 209)); // Grey
                        if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                        if (numberText != null) numberText.Visibility = Visibility.Visible;
                    }
                    break;
                    
                case OnboardingStepStatus.NotStarted:
                default:
                    circleBorder.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 209, 209)); // Grey
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
                    if (gitInstalled)
                    {
                        // Show green text if Git is installed, regardless of step status
                        var gitVersion = await _gitService.GetVersionAsync();
                        GitFoundText.Text = $"√ Git version {gitVersion} detected";
                        GitFoundText.Visibility = Visibility.Visible;
                        GitNotFoundPanel.Visibility = Visibility.Collapsed;
                    }
                    else if (statuses[1] == OnboardingStepStatus.Current)
                    {
                        // Only show "not found" panel when it's the current step and Git is not installed
                        GitFoundText.Visibility = Visibility.Collapsed;
                        GitNotFoundPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // For pending steps when Git is not installed
                        GitFoundText.Visibility = Visibility.Collapsed;
                        GitNotFoundPanel.Visibility = Visibility.Collapsed;
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
                            gitConfigText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)); // Green
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
                        repoStatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)); // Green
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
                        usageModeText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)); // Green
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
            
            // Mark language as configured using safe method
            await _onboardingService.SetConfigurationValueAsync("LanguageConfigured", true);
            await _onboardingService.CompleteStep(0);
        }

        private async void SkipLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            // Skip language configuration and move to next step
            await _onboardingService.SetConfigurationValueAsync("LanguageConfigured", true);
            await _onboardingService.CompleteStep(0);
        }

        private async void UseLocallyButton_Click(object sender, RoutedEventArgs e)
        {
            // Configure for local use only
            await _onboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
            await _onboardingService.CompleteStep(3);
            
            // Show confirmation
            var dialog = new ContentDialog
            {
                Title = "Local Mode Activated",
                Content = "GitMC will now operate in local mode. Your saves will be managed locally with Git version control.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
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
                        await _onboardingService.SetConfigurationValueAsync("SaveAdded", true);
                        await _onboardingService.CompleteStep(4);
                    }
                    else
                    {
                        // Show error message
                        var dialog = new ContentDialog
                        {
                            Title = "Invalid Minecraft Save",
                            Content = "The selected folder doesn't appear to be a valid Minecraft save. A valid save should contain level.dat or level.dat_old.",
                            CloseButtonText = "OK",
                            XamlRoot = XamlRoot
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
                var statuses = _onboardingService.StepStatuses;
                
                if (gitInstalled)
                {
                    var gitVersion = await _gitService.GetVersionAsync();
                    GitStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                    GitStatusText.Text = $"Git v{gitVersion} installed and ready";
                    
                    // Change icon to checkmark when completed
                    if (statuses.Length > 1 && statuses[1] == OnboardingStepStatus.Completed)
                    {
                        var gitIconControl = GitStatusIcon.Child as FontIcon;
                        if (gitIconControl != null)
                        {
                            gitIconControl.Glyph = "\uE73E"; // Checkmark
                        }
                    }
                }
                else
                {
                    GitStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                    GitStatusText.Text = "Git is not installed. Please install Git to continue.";
                    
                    // Keep original icon when not completed
                    var gitIconControl = GitStatusIcon.Child as FontIcon;
                    if (gitIconControl != null)
                    {
                        gitIconControl.Glyph = "\uE896"; // Download/Install icon
                    }
                }

                // Check Git identity
                var (userName, userEmail) = await _gitService.GetIdentityAsync();
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail))
                {
                    GitIdentityStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                    GitIdentityStatusText.Text = $"Configured as {userName} ({userEmail})";
                    
                    // Change icon to checkmark when completed
                    if (statuses.Length > 2 && statuses[2] == OnboardingStepStatus.Completed)
                    {
                        var identityIconControl = GitIdentityStatusIcon.Child as FontIcon;
                        if (identityIconControl != null)
                        {
                            identityIconControl.Glyph = "\uE73E"; // Checkmark
                        }
                    }
                }
                else
                {
                    GitIdentityStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                    GitIdentityStatusText.Text = "Git identity not configured. Set your name and email.";
                    
                    // Keep original icon when not completed
                    var identityIconControl = GitIdentityStatusIcon.Child as FontIcon;
                    if (identityIconControl != null)
                    {
                        identityIconControl.Glyph = "\uE7BA"; // Person icon
                    }
                }

                // Check Platform Connection
                if (statuses.Length > 3 && statuses[3] == OnboardingStepStatus.Completed)
                {
                    PlatformStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                    PlatformStatusText.Text = "Platform connected";
                    
                    var platformIconControl = PlatformStatusIcon.Child as FontIcon;
                    if (platformIconControl != null)
                    {
                        platformIconControl.Glyph = "\uE73E"; // Checkmark
                    }
                }
                else
                {
                    PlatformStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                    PlatformStatusText.Text = "Not connected";
                    
                    var platformIconControl = PlatformStatusIcon.Child as FontIcon;
                    if (platformIconControl != null)
                    {
                        platformIconControl.Glyph = "\uE8A7"; // Cloud icon
                    }
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
            var uri = new Uri("https://git-scm.com/downloads");
            await Launcher.LaunchUriAsync(uri);
        }

        private async void ConnectPlatformButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PlatformSelector.SelectedItem == GitHubSelectorItem) // GitHub
                {
                    await ConnectToGitHub();
                }
                else // Self-hosting
                {
                    await ConnectToSelfHostedGit();
                }
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Connection Error",
                    Content = $"Failed to connect: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private void PlatformSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            // Toggle visibility of configuration panels based on selection
            if (GitHubConfigPanel != null && SelfHostingConfigPanel != null)
            {
                if (PlatformSelector.SelectedItem == GitHubSelectorItem) // GitHub
                {
                    GitHubConfigPanel.Visibility = Visibility.Visible;
                    SelfHostingConfigPanel.Visibility = Visibility.Collapsed;
                }
                else // Self-hosting
                {
                    GitHubConfigPanel.Visibility = Visibility.Collapsed;
                    SelfHostingConfigPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate input fields
                if (string.IsNullOrWhiteSpace(SelfHostingUrlBox.Text) ||
                    string.IsNullOrWhiteSpace(SelfHostingUsernameBox.Text) ||
                    string.IsNullOrWhiteSpace(SelfHostingTokenBox.Password))
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Invalid Input",
                        Content = "Please fill in all required fields (URL, Username, and Access Token).",
                        CloseButtonText = "OK",
                        XamlRoot = XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                // Disable button and show progress
                TestConnectionButton.IsEnabled = false;
                TestConnectionButton.Content = "Testing...";

                // TODO: Implement actual connection test
                await Task.Delay(2000); // Simulate network request

                var successDialog = new ContentDialog
                {
                    Title = "Connection Successful",
                    Content = "Successfully connected to your Git server!",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Connection Failed",
                    Content = $"Failed to connect to Git server: {ex.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
                TestConnectionButton.Content = "Test Connection";
            }
        }

        private async Task ConnectToGitHub()
        {
            // GitHub OAuth flow simulation
            var dialog = new ContentDialog
            {
                Title = "Connect to GitHub",
                Content = "This will open GitHub's authorization page in your browser. Continue?",
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // TODO: Implement actual GitHub OAuth
                // For now, simulate successful connection
                await Task.Delay(1000);

                // Store GitHub configuration
                _configurationService.SelectedPlatform = "GitHub";
                _configurationService.GitHubUsername = "NaughtyChas"; // Would come from OAuth
                _configurationService.GitHubAccessToken = "[simulated_token]"; // Would come from OAuth
                _configurationService.GitHubRepository = GitHubRepoNameBox?.Text ?? "minecraft-saves";
                _configurationService.GitHubPrivateRepo = GitHubPrivateRepoBox?.IsChecked ?? true;

                // Update UI to show connected state
                GitHubStatusText.Text = $"Connected as {_configurationService.GitHubUsername}";
                GitHubStatusPanel.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 255, 220));
                GitHubRepoSettings.Visibility = Visibility.Visible;

                // Mark platform as configured
                await _onboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
                await _onboardingService.CompleteStep(3);

                // Update system status to reflect the changes
                UpdateSystemStatus();

                var successDialog = new ContentDialog
                {
                    Title = "GitHub Connected",
                    Content = "Successfully connected to GitHub! You can now create repositories for your saves.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await successDialog.ShowAsync();
            }
        }

        private async Task ConnectToSelfHostedGit()
        {
            // Validate self-hosting configuration
            if (string.IsNullOrWhiteSpace(SelfHostingUrlBox?.Text) ||
                string.IsNullOrWhiteSpace(SelfHostingUsernameBox?.Text) ||
                string.IsNullOrWhiteSpace(SelfHostingTokenBox?.Password))
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Missing Configuration",
                    Content = "Please fill in all required fields before connecting.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
                return;
            }

            // TODO: Implement actual Git server connection
            // For now, simulate successful connection
            await Task.Delay(1500);

            // Store self-hosted configuration
            _configurationService.SelectedPlatform = "Self-Hosted";
            _configurationService.GitServerUrl = SelfHostingUrlBox?.Text ?? "";
            _configurationService.GitUsername = SelfHostingUsernameBox?.Text ?? "";
            _configurationService.GitAccessToken = SelfHostingTokenBox?.Password ?? "";
            _configurationService.GitServerType = "Custom"; // Could be detected from URL

            // Mark platform as configured
            await _onboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
            await _onboardingService.CompleteStep(3);

            // Update system status to reflect the changes
            UpdateSystemStatus();

            var successDialog = new ContentDialog
            {
                Title = "Git Server Connected",
                Content = "Successfully connected to your Git server!",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await successDialog.ShowAsync();
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
                            XamlRoot = XamlRoot
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
    }
}
