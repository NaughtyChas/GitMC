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

        // Property to determine which view to show
        public bool HasManagedSaves => GetManagedSavesCount() > 0;
        public bool ShowOnboarding => !HasManagedSaves;

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

            // Notify property change for conditional view logic
            OnPropertyChanged(nameof(HasManagedSaves));
            OnPropertyChanged(nameof(ShowOnboarding));
        }

        private int GetManagedSavesCount()
        {
            // For now, check if the "Add Save" step (step 4) is completed
            // This indicates that at least one save has been added
            var statuses = _onboardingService.StepStatuses;
            if (statuses.Length > 4 && statuses[4] == OnboardingStepStatus.Completed)
            {
                return 1; // At least one save exists
            }
            return 0;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
            // Check if we should show save management or onboarding
            var showSaveManagement = HasManagedSaves;

            // Update main content visibility
            if (FindName("OnboardingContent") is FrameworkElement onboardingContent)
            {
                onboardingContent.Visibility = showSaveManagement ? Visibility.Collapsed : Visibility.Visible;
            }

            if (FindName("SaveManagementContent") is FrameworkElement saveManagementContent)
            {
                saveManagementContent.Visibility = showSaveManagement ? Visibility.Visible : Visibility.Collapsed;
            }

            // Only update onboarding steps if showing onboarding view
            if (!showSaveManagement)
            {
                var currentStep = _onboardingService.CurrentStepIndex;
                var statuses = _onboardingService.StepStatuses;

                // Update step displays (only if elements exist)
                UpdateStepDisplaySafe(0, "Step1Content", "Step1CollapsedContent", currentStep, statuses);
                UpdateStepDisplaySafe(1, "Step2Content", "Step2CollapsedContent", currentStep, statuses);
                UpdateStepDisplaySafe(2, "Step3Content", "Step3CollapsedContent", currentStep, statuses);
                UpdateStepDisplaySafe(3, "Step4Content", "Step4CollapsedContent", currentStep, statuses);
                UpdateStepDisplaySafe(4, "Step5Content", "Step5CollapsedContent", currentStep, statuses);

                // Update step indicators (only if elements exist)
                UpdateStepIndicatorSafe("Step1Circle", "Step1CheckIcon", "Step1NumberText", 0, statuses);
                UpdateStepIndicatorSafe("Step2Circle", "Step2CheckIcon", "Step2NumberText", 1, statuses);
                UpdateStepIndicatorSafe("Step3Circle", "Step3CheckIcon", "Step3NumberText", 2, statuses);
                UpdateStepIndicatorSafe("Step4Circle", "Step4CheckIcon", "Step4NumberText", 3, statuses);
                UpdateStepIndicatorSafe("Step5Circle", "Step5CheckIcon", "Step5NumberText", 4, statuses);

                // Update step status texts
                UpdateStepStatusTexts();
            }
        }

        private void UpdateStepDisplaySafe(int stepIndex, string fullContentName, string collapsedContentName, int currentStep, OnboardingStepStatus[] statuses)
        {
            var fullContent = FindName(fullContentName) as FrameworkElement;
            var collapsedContent = FindName(collapsedContentName) as FrameworkElement;

            if (fullContent != null && collapsedContent != null)
            {
                UpdateStepDisplay(stepIndex, fullContent, collapsedContent, currentStep, statuses);
            }
        }

        private void UpdateStepIndicatorSafe(string circleName, string checkIconName, string numberTextName, int stepIndex, OnboardingStepStatus[] statuses)
        {
            var circleBorder = FindName(circleName) as Border;
            var checkIcon = FindName(checkIconName) as FontIcon;
            var numberText = FindName(numberTextName) as TextBlock;

            if (circleBorder != null)
            {
                UpdateStepIndicator(circleBorder, checkIcon, numberText, stepIndex, statuses);
            }
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
                // Only update if we're showing the onboarding view
                if (ShowOnboarding)
                {
                    var statuses = _onboardingService.StepStatuses;

                    // Step 2: Git Installation
                    if (statuses.Length > 1)
                    {
                        bool gitInstalled = await _gitService.IsInstalledAsync();
                        var gitFoundText = FindName("GitFoundText") as TextBlock;
                        var gitNotFoundPanel = FindName("GitNotFoundPanel") as StackPanel;

                        if (gitFoundText != null && gitNotFoundPanel != null)
                        {
                            if (gitInstalled)
                            {
                                // Show green text if Git is installed, regardless of step status
                                var gitVersion = await _gitService.GetVersionAsync();
                                gitFoundText.Text = $"√ Git version {gitVersion} detected";
                                gitFoundText.Visibility = Visibility.Visible;
                                gitNotFoundPanel.Visibility = Visibility.Collapsed;
                            }
                            else if (statuses[1] == OnboardingStepStatus.Current)
                            {
                                // Only show "not found" panel when it's the current step and Git is not installed
                                gitFoundText.Visibility = Visibility.Collapsed;
                                gitNotFoundPanel.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                // For pending steps when Git is not installed
                                gitFoundText.Visibility = Visibility.Collapsed;
                                gitNotFoundPanel.Visibility = Visibility.Collapsed;
                            }
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
            // Show confirmation flyout first, only proceed if user clicks OK
            var confirmed = await ShowConfirmationFlyoutWithOK(sender as FrameworkElement, "Local Mode Activated",
                "GitMC will now operate in local mode. Your saves will be managed locally with Git version control.");

            if (confirmed)
            {
                // Configure for local use only after user confirmation
                await _onboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
                await _onboardingService.CompleteStep(3);
            }
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
            var loadingPanel = FindName("LoadingPanel") as Grid;
            var loadingProgressRing = FindName("LoadingProgressRing") as ProgressBar;

            if (loadingPanel != null && loadingProgressRing != null)
            {
                loadingPanel.Visibility = Visibility.Visible;
                loadingProgressRing.IsIndeterminate = true;
            }

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

                        // Update the view to show save management
                        OnPropertyChanged(nameof(HasManagedSaves));
                        OnPropertyChanged(nameof(ShowOnboarding));
                        UpdateStepVisibility();
                    }
                    else
                    {
                        // Show error message with flyout
                        ShowErrorFlyout(sender as FrameworkElement, "Invalid Minecraft Save",
                            "The selected folder doesn't appear to be a valid Minecraft save. A valid save should contain level.dat or level.dat_old.");
                    }
                }
            }
            finally
            {
                if (loadingPanel != null && loadingProgressRing != null)
                {
                    loadingPanel.Visibility = Visibility.Collapsed;
                    loadingProgressRing.IsIndeterminate = false;
                }
            }
        }

        // Update the real-time status checking
        private async void UpdateSystemStatus()
        {
            try
            {
                // Only update status elements if we're showing the onboarding view
                if (ShowOnboarding)
                {
                    // Check Git installation
                    var gitInstalled = await _gitService.IsInstalledAsync();
                    var statuses = _onboardingService.StepStatuses;

                    var gitStatusIcon = FindName("GitStatusIcon") as Border;
                    var gitStatusText = FindName("GitStatusText") as TextBlock;

                    if (gitStatusIcon != null && gitStatusText != null)
                    {
                        if (gitInstalled)
                        {
                            var gitVersion = await _gitService.GetVersionAsync();
                            gitStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                            gitStatusText.Text = $"Git v{gitVersion} installed and ready";

                            // Change icon to checkmark when completed
                            if (statuses.Length > 1 && statuses[1] == OnboardingStepStatus.Completed)
                            {
                                var gitIconControl = gitStatusIcon.Child as FontIcon;
                                if (gitIconControl != null)
                                {
                                    gitIconControl.Glyph = "\uE73E"; // Checkmark
                                }
                            }
                        }
                        else
                        {
                            gitStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                            gitStatusText.Text = "Git is not installed. Please install Git to continue.";

                            // Keep original icon when not completed
                            var gitIconControl = gitStatusIcon.Child as FontIcon;
                            if (gitIconControl != null)
                            {
                                gitIconControl.Glyph = "\uE896"; // Download/Install icon
                            }
                        }
                    }

                    // Check Git identity
                    var gitIdentityStatusIcon = FindName("GitIdentityStatusIcon") as Border;
                    var gitIdentityStatusText = FindName("GitIdentityStatusText") as TextBlock;

                    if (gitIdentityStatusIcon != null && gitIdentityStatusText != null)
                    {
                        var (userName, userEmail) = await _gitService.GetIdentityAsync();
                        if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail))
                        {
                            gitIdentityStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                            gitIdentityStatusText.Text = $"Configured as {userName} ({userEmail})";

                            // Change icon to checkmark when completed
                            if (statuses.Length > 2 && statuses[2] == OnboardingStepStatus.Completed)
                            {
                                var identityIconControl = gitIdentityStatusIcon.Child as FontIcon;
                                if (identityIconControl != null)
                                {
                                    identityIconControl.Glyph = "\uE73E"; // Checkmark
                                }
                            }
                        }
                        else
                        {
                            gitIdentityStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                            gitIdentityStatusText.Text = "Git identity not configured. Set your name and email.";

                            // Keep original icon when not completed
                            var identityIconControl = gitIdentityStatusIcon.Child as FontIcon;
                            if (identityIconControl != null)
                            {
                                identityIconControl.Glyph = "\uE7BA"; // Person icon
                            }
                        }
                    }

                    // Check Platform Connection
                    var platformStatusIcon = FindName("PlatformStatusIcon") as Border;
                    var platformStatusText = FindName("PlatformStatusText") as TextBlock;

                    if (platformStatusIcon != null && platformStatusText != null)
                    {
                        if (statuses.Length > 3 && statuses[3] == OnboardingStepStatus.Completed)
                        {
                            platformStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
                            platformStatusText.Text = "Platform connected";

                            var platformIconControl = platformStatusIcon.Child as FontIcon;
                            if (platformIconControl != null)
                            {
                                platformIconControl.Glyph = "\uE73E"; // Checkmark
                            }
                        }
                        else
                        {
                            platformStatusIcon.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 140, 0));
                            platformStatusText.Text = "Not connected";

                            var platformIconControl = platformStatusIcon.Child as FontIcon;
                            if (platformIconControl != null)
                            {
                                platformIconControl.Glyph = "\uE8A7"; // Cloud icon
                            }
                        }
                    }

                    // Update overall progress
                    var overallProgressBar = FindName("OverallProgressBar") as ProgressBar;
                    var progressText = FindName("ProgressText") as TextBlock;

                    if (overallProgressBar != null && progressText != null)
                    {
                        var completedSteps = _onboardingService.StepStatuses.Count(s => s == OnboardingStepStatus.Completed);
                        var totalSteps = _onboardingService.StepStatuses.Length;
                        var progressPercentage = totalSteps > 0 ? (completedSteps * 100.0 / totalSteps) : 0;

                        overallProgressBar.Value = progressPercentage;
                        progressText.Text = $"{completedSteps} of {totalSteps} steps completed";
                    }
                }
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
                    await ConnectToGitHub(sender as FrameworkElement);
                }
                else // Self-hosting
                {
                    await ConnectToSelfHostedGit(sender as FrameworkElement);
                }
            }
            catch (Exception ex)
            {
                ShowErrorFlyout(sender as FrameworkElement, "Connection Error", $"Failed to connect: {ex.Message}");
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
                    ShowErrorFlyout(sender as FrameworkElement, "Invalid Input",
                        "Please fill in all required fields (URL, Username, and Access Token).");
                    return;
                }

                // Disable button and show progress
                TestConnectionButton.IsEnabled = false;
                TestConnectionButton.Content = "Testing...";

                // TODO: Implement actual connection test
                await Task.Delay(2000); // Simulate network request

                ShowSuccessFlyout(sender as FrameworkElement, "Connection Successful",
                    "Successfully connected to your Git server!");
            }
            catch (Exception ex)
            {
                ShowErrorFlyout(sender as FrameworkElement, "Connection Failed",
                    $"Failed to connect to Git server: {ex.Message}");
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
                TestConnectionButton.Content = "Test Connection";
            }
        }

        private async Task ConnectToGitHub(FrameworkElement? anchor = null)
        {
            // GitHub OAuth flow simulation - use flyout with buttons for confirmation
            var confirmed = await ShowConfirmationFlyoutWithResult(
                anchor ?? this, // Use the anchor button or fallback to page
                "Connect to GitHub",
                "This will open GitHub's authorization page in your browser. Continue?",
                "Continue",
                "Cancel"
            );

            if (confirmed)
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

                ShowSuccessFlyout(anchor ?? this, "GitHub Connected",
                    "Successfully connected to GitHub! You can now create repositories for your saves.");
            }
        }

        private async Task ConnectToSelfHostedGit(FrameworkElement? anchor = null)
        {
            // Validate self-hosting configuration
            if (string.IsNullOrWhiteSpace(SelfHostingUrlBox?.Text) ||
                string.IsNullOrWhiteSpace(SelfHostingUsernameBox?.Text) ||
                string.IsNullOrWhiteSpace(SelfHostingTokenBox?.Password))
            {
                ShowErrorFlyout(anchor ?? this, "Missing Configuration",
                    "Please fill in all required fields before connecting.");
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

            ShowSuccessFlyout(anchor ?? this, "Git Server Connected",
                "Successfully connected to your Git server!");
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
                        ShowErrorFlyout(sender as FrameworkElement, "Invalid Folder",
                            "The selected folder doesn't appear to contain valid Minecraft data.");
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

        // Flyout helper methods for less intrusive dialogs
        private void ShowConfirmationFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            FontSize = 16,
                            Margin = new Thickness(0, 0, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            flyout.ShowAt(anchor);
        }

        private async Task<bool> ShowConfirmationFlyoutWithOK(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return false;

            var tcs = new TaskCompletionSource<bool>();

            var okButton = new Button
            {
                Content = "OK",
                Style = Application.Current.Resources["AccentButtonStyle"] as Style,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            FontSize = 16,
                            Margin = new Thickness(0, 0, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) =>
            {
                tcs.SetResult(true);
                flyout.Hide();
            };

            flyout.ShowAt(anchor);
            return await tcs.Task;
        }

        private void ShowErrorFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 8),
                            Children =
                            {
                                new FontIcon
                                {
                                    Glyph = "\uE783", // Error icon
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 52, 56)),
                                    Margin = new Thickness(0, 0, 8, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = title,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    FontSize = 16,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            flyout.ShowAt(anchor);
        }

        private void ShowSuccessFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 8),
                            Children =
                            {
                                new FontIcon
                                {
                                    Glyph = "\uE73E", // Checkmark icon
                                    FontSize = 16,
                                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)),
                                    Margin = new Thickness(0, 0, 8, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text = title,
                                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                    FontSize = 16,
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            flyout.ShowAt(anchor);
        }

        private async Task<bool> ShowConfirmationFlyoutWithResult(FrameworkElement? anchor, string title, string message, string confirmText = "Continue", string cancelText = "Cancel")
        {
            if (anchor == null) return false;

            var tcs = new TaskCompletionSource<bool>();

            var confirmButton = new Button
            {
                Content = confirmText,
                Style = Application.Current.Resources["AccentButtonStyle"] as Style,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            FontSize = 16,
                            Margin = new Thickness(0, 0, 0, 8)
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Children = { confirmButton, cancelButton }
                        }
                    }
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Right
            };

            confirmButton.Click += (s, e) =>
            {
                tcs.SetResult(true);
                flyout.Hide();
            };

            cancelButton.Click += (s, e) =>
            {
                tcs.SetResult(false);
                flyout.Hide();
            };

            flyout.ShowAt(anchor);
            return await tcs.Task;
        }
    }
}
