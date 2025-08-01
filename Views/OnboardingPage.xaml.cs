using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using GitMC.Constants;
using GitMC.Helpers;
using GitMC.Models;
using GitMC.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace GitMC.Views;

public sealed partial class OnboardingPage : Page, INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private readonly ManagedSaveService _managedSaveService;
    private readonly IMinecraftAnalyzerService _minecraftAnalyzerService;
    private readonly NbtService _nbtService;

    public OnboardingPage()
    {
        InitializeComponent();
        _nbtService = new NbtService();
        _gitService = new GitService();
        _configurationService = new ConfigurationService();
        _dataStorageService = new DataStorageService();
        _minecraftAnalyzerService = new MinecraftAnalyzerService(_nbtService);
        _managedSaveService = new ManagedSaveService(_dataStorageService);
        OnboardingService = new OnboardingService(_gitService, _configurationService);

        // Subscribe to onboarding changes
        OnboardingService.PropertyChanged += OnboardingService_PropertyChanged;

        DataContext = this;
        Loaded += OnboardingPage_Loaded;
    }

    public IOnboardingService OnboardingService { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void OnboardingPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize the configuration service and onboarding
            await OnboardingService.InitializeAsync();
            try
            {
                OnboardingService.RefreshApplicationDataCache();
            }
            catch
            {
                // If cache refresh fails, continue anyway
            }

            // Then refresh onboarding status on page load
            await OnboardingService.RefreshAllSteps();
            UpdateStepVisibility();
            UpdateSystemStatus();
        }
        catch (Exception ex)
        {
            // Log error and show user-friendly message
            Debug.WriteLine($"Error loading onboarding page: {ex.Message}");
        }
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
        int currentStep = OnboardingService.CurrentStepIndex;
        OnboardingStepStatus[] statuses = OnboardingService.StepStatuses;

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

    private void UpdateStepDisplaySafe(int stepIndex, string fullContentName, string collapsedContentName,
        int currentStep, OnboardingStepStatus[] statuses)
    {
        var fullContent = FindName(fullContentName) as FrameworkElement;
        var collapsedContent = FindName(collapsedContentName) as FrameworkElement;

        if (fullContent != null && collapsedContent != null)
            UpdateStepDisplay(stepIndex, fullContent, collapsedContent, currentStep, statuses);
    }

    private void UpdateStepIndicatorSafe(string circleName, string checkIconName, string numberTextName, int stepIndex,
        OnboardingStepStatus[] statuses)
    {
        var circleBorder = FindName(circleName) as Border;
        var checkIcon = FindName(checkIconName) as FontIcon;
        var numberText = FindName(numberTextName) as TextBlock;

        if (circleBorder != null) UpdateStepIndicator(circleBorder, checkIcon, numberText, stepIndex, statuses);
    }

    private void UpdateStepDisplay(int stepIndex, FrameworkElement? fullContent, FrameworkElement? collapsedContent,
        int currentStep, OnboardingStepStatus[] statuses)
    {
        if (stepIndex >= statuses.Length || fullContent == null || collapsedContent == null) return;

        // Check if all previous steps are completed
        bool canBeActive = stepIndex == 0 || statuses.Take(stepIndex).All(s => s == OnboardingStepStatus.Completed);
        bool isCurrentStep = stepIndex == currentStep && statuses[stepIndex] == OnboardingStepStatus.Current &&
                             canBeActive;

        fullContent.Visibility = isCurrentStep ? Visibility.Visible : Visibility.Collapsed;
        collapsedContent.Visibility = isCurrentStep ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateStepIndicator(Border? circleBorder, FontIcon? checkIcon, TextBlock? numberText, int stepIndex,
        OnboardingStepStatus[] statuses)
    {
        if (stepIndex >= statuses.Length || circleBorder == null) return;

        OnboardingStepStatus status = statuses[stepIndex];
        int currentStep = OnboardingService.CurrentStepIndex;

        // Check if all previous steps are completed
        bool canBeActive = stepIndex == 0 || (stepIndex <= currentStep &&
                                              statuses.Take(stepIndex).All(s => s == OnboardingStepStatus.Completed));

        switch (status)
        {
            case OnboardingStepStatus.Completed:
                circleBorder.Background = new SolidColorBrush(ColorConstants.SuccessGreen); // Green
                if (checkIcon != null) checkIcon.Visibility = Visibility.Visible;
                if (numberText != null) numberText.Visibility = Visibility.Collapsed;
                break;

            case OnboardingStepStatus.Current:
                if (canBeActive)
                {
                    circleBorder.Background = new SolidColorBrush(ColorConstants.InfoBlue); // Blue
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                    if (numberText != null) numberText.Visibility = Visibility.Visible;
                }
                else
                {
                    // Show as not started if previous steps aren't completed
                    circleBorder.Background = new SolidColorBrush(ColorConstants.DisabledText); // Grey
                    if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                    if (numberText != null) numberText.Visibility = Visibility.Visible;
                }

                break;

            case OnboardingStepStatus.NotStarted:
            default:
                circleBorder.Background = new SolidColorBrush(ColorConstants.DisabledText); // Grey
                if (checkIcon != null) checkIcon.Visibility = Visibility.Collapsed;
                if (numberText != null) numberText.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void UpdateStepStatusTexts()
    {
        try
        {
            OnboardingStepStatus[] statuses = OnboardingService.StepStatuses;

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
                        string gitVersion = await _gitService.GetVersionAsync();
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
                (string? userName, string? userEmail) = await _gitService.GetIdentityAsync();
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail) &&
                    statuses[2] == OnboardingStepStatus.Completed)
                {
                    // Find Git config status text element and update it
                    var gitConfigText = FindName("GitConfigCompletedText") as TextBlock;
                    if (gitConfigText != null)
                    {
                        gitConfigText.Text = $"✓ Configured as {userName} ({userEmail})";
                        gitConfigText.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen); // Green
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
                    repoStatusText.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen); // Green
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
                    usageModeText.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen); // Green
                    usageModeText.Visibility = Visibility.Visible;
                }
            }
        }
        catch { }
    }

    private async void LanguageSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to language settings
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(SettingsPage));

        // Mark language as configured using safe method
        await OnboardingService.SetConfigurationValueAsync("LanguageConfigured", true);
        await OnboardingService.CompleteStep(0);
    }

    private async void SkipLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        // Skip language configuration and move to next step
        await OnboardingService.SetConfigurationValueAsync("LanguageConfigured", true);
        await OnboardingService.CompleteStep(0);
    }

    private async void UseLocallyButton_Click(object sender, RoutedEventArgs e)
    {
        // Show confirmation flyout first, only proceed if user clicks OK
        bool confirmed = await ShowConfirmationFlyoutWithOk(sender as FrameworkElement, "Local Mode Activated",
            "GitMC will now operate in local mode. Your saves will be managed locally with Git version control.");

        if (confirmed)
        {
            // Configure for local use only after user confirmation
            await OnboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
            await OnboardingService.CompleteStep(3);
        }
    }

    private async void GitConfigButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to Git configuration settings
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(SettingsPage));

        // Check if Git is configured after navigation
        await Task.Delay(1000); // Small delay to allow settings page to potentially configure Git
        await OnboardingService.RefreshAllSteps();
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
                IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                MinecraftSave? save = await _minecraftAnalyzerService.AnalyzeSaveFolder(folder.Path);
                if (save != null)
                {
                    // Add to navigation in MainWindow
                    if (App.MainWindow is MainWindow mainWindow) mainWindow.AddSaveToNavigation(save.Name, save.Path);

                    // Register this save in our managed saves system using the service
                    try
                    {
                        await _managedSaveService.RegisterManagedSave(save);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to register managed save: {ex.Message}");
                        // Show warning but continue - save is still added to navigation
                        FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Save Registration Warning",
                            "The save was added but there was an issue registering it for management. You may need to add it again later.");
                    }

                    // Complete step 4 (save will be detected by OnboardingService automatically)
                    await OnboardingService.CompleteStep(4);

                    // Navigate to save management page after adding save
                    if (App.MainWindow is MainWindow window) window.NavigateToPage(typeof(SaveManagementPage));
                }
                else
                {
                    // Show error message with flyout
                    FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Invalid Minecraft Save",
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

    private async void AddVersionFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // Similar to AddSaveButton_Click but specifically for version folders
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
                IntPtr hwnd = WindowNative.GetWindowHandle(App.MainWindow);
                InitializeWithWindow.Initialize(folderPicker, hwnd);
            }

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                // For version folders, we might have different validation logic
                MinecraftSave? save = await _minecraftAnalyzerService.AnalyzeSaveFolder(folder.Path);
                if (save != null)
                {
                    if (App.MainWindow is MainWindow mainWindow) mainWindow.AddSaveToNavigation(save.Name, save.Path);

                    // Register this save in our managed saves system using the service
                    try
                    {
                        await _managedSaveService.RegisterManagedSave(save);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to register managed save: {ex.Message}");
                        // Show warning but continue - save is still added to navigation
                        FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Save Registration Warning",
                            "The save was added but there was an issue registering it for management. You may need to add it again later.");
                    }

                    // Complete step 4 (save will be detected by OnboardingService automatically)
                    await OnboardingService.CompleteStep(4);

                    // Navigate to save management page after adding save
                    if (App.MainWindow is MainWindow window) window.NavigateToPage(typeof(SaveManagementPage));
                }
                else
                {
                    FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Invalid Folder",
                        "The selected folder doesn't appear to contain valid Minecraft data.");
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
            // Check Git installation
            bool gitInstalled = await _gitService.IsInstalledAsync();
            OnboardingStepStatus[] statuses = OnboardingService.StepStatuses;

            var gitStatusIcon = FindName("GitStatusIcon") as Border;
            var gitStatusText = FindName("GitStatusText") as TextBlock;

            if (gitStatusIcon != null && gitStatusText != null)
            {
                if (gitInstalled)
                {
                    string gitVersion = await _gitService.GetVersionAsync();
                    gitStatusIcon.Background = new SolidColorBrush(ColorConstants.SuccessGreen);
                    gitStatusText.Text = $"Git v{gitVersion} installed and ready";

                    // Change icon to checkmark when completed
                    if (statuses.Length > 1 && statuses[1] == OnboardingStepStatus.Completed)
                    {
                        var gitIconControl = gitStatusIcon.Child as FontIcon;
                        if (gitIconControl != null) gitIconControl.Glyph = "\uE73E"; // Checkmark
                    }
                }
                else
                {
                    gitStatusIcon.Background = new SolidColorBrush(ColorConstants.WarningOrangeBright);
                    gitStatusText.Text = "Git is not installed. Please install Git to continue.";

                    // Keep original icon when not completed
                    var gitIconControl = gitStatusIcon.Child as FontIcon;
                    if (gitIconControl != null) gitIconControl.Glyph = "\uE896"; // Download/Install icon
                }
            }

            // Check Git identity
            var gitIdentityStatusIcon = FindName("GitIdentityStatusIcon") as Border;
            var gitIdentityStatusText = FindName("GitIdentityStatusText") as TextBlock;

            if (gitIdentityStatusIcon != null && gitIdentityStatusText != null)
            {
                (string? userName, string? userEmail) = await _gitService.GetIdentityAsync();
                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail))
                {
                    gitIdentityStatusIcon.Background = new SolidColorBrush(ColorConstants.SuccessGreen);
                    gitIdentityStatusText.Text = $"Configured as {userName} ({userEmail})";

                    // Change icon to checkmark when completed
                    if (statuses.Length > 2 && statuses[2] == OnboardingStepStatus.Completed)
                    {
                        var identityIconControl = gitIdentityStatusIcon.Child as FontIcon;
                        if (identityIconControl != null) identityIconControl.Glyph = "\uE73E"; // Checkmark
                    }
                }
                else
                {
                    gitIdentityStatusIcon.Background = new SolidColorBrush(ColorConstants.WarningOrangeBright);
                    gitIdentityStatusText.Text = "Git identity not configured. Set your name and email.";

                    // Keep original icon when not completed
                    var identityIconControl = gitIdentityStatusIcon.Child as FontIcon;
                    if (identityIconControl != null) identityIconControl.Glyph = "\uE7BA"; // Person icon
                }
            }

            // Check Platform Connection
            var platformStatusIcon = FindName("PlatformStatusIcon") as Border;
            var platformStatusText = FindName("PlatformStatusText") as TextBlock;

            if (platformStatusIcon != null && platformStatusText != null)
            {
                if (statuses.Length > 3 && statuses[3] == OnboardingStepStatus.Completed)
                {
                    platformStatusIcon.Background = new SolidColorBrush(ColorConstants.SuccessGreen);
                    platformStatusText.Text = "Platform connected";

                    var platformIconControl = platformStatusIcon.Child as FontIcon;
                    if (platformIconControl != null) platformIconControl.Glyph = "\uE73E"; // Checkmark
                }
                else
                {
                    platformStatusIcon.Background = new SolidColorBrush(ColorConstants.WarningOrangeBright);
                    platformStatusText.Text = "Not connected";

                    var platformIconControl = platformStatusIcon.Child as FontIcon;
                    if (platformIconControl != null) platformIconControl.Glyph = "\uE8A7"; // Cloud icon
                }
            }

            // Update overall progress
            var overallProgressBar = FindName("OverallProgressBar") as ProgressBar;
            var progressText = FindName("ProgressText") as TextBlock;

            if (overallProgressBar != null && progressText != null)
            {
                int completedSteps = OnboardingService.StepStatuses.Count(s => s == OnboardingStepStatus.Completed);
                int totalSteps = OnboardingService.StepStatuses.Length;
                double progressPercentage = totalSteps > 0 ? completedSteps * 100.0 / totalSteps : 0;

                overallProgressBar.Value = progressPercentage;
                progressText.Text = $"{completedSteps} of {totalSteps} steps completed";
            }
        }
        catch { }
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
                await ConnectToGitHub(sender as FrameworkElement);
            else // Self-hosting
                await ConnectToSelfHostedGit(sender as FrameworkElement);
        }
        catch (Exception ex)
        {
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Connection Error",
                $"Failed to connect: {ex.Message}");
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
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Invalid Input",
                    "Please fill in all required fields (URL, Username, and Access Token).");
                return;
            }

            // Disable button and show progress
            TestConnectionButton.IsEnabled = false;
            TestConnectionButton.Content = "Testing...";

            // TODO: Implement actual connection test
            await Task.Delay(2000); // Simulate network request

            FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Connection Successful",
                "Successfully connected to your Git server!");
        }
        catch (Exception ex)
        {
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Connection Failed",
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
        bool confirmed = await FlyoutHelper.ShowConfirmationFlyout(
            anchor ?? this, // Use the anchor button or fallback to page
            "Connect to GitHub",
            "This will open GitHub's authorization page in your browser. Continue?"
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
            GitHubStatusPanel.Background = new SolidColorBrush(ColorConstants.SuccessBackgroundLight);
            GitHubRepoSettings.Visibility = Visibility.Visible;

            // Mark platform as configured
            await OnboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
            await OnboardingService.CompleteStep(3);

            // Update system status to reflect the changes
            UpdateSystemStatus();

            FlyoutHelper.ShowSuccessFlyout(anchor ?? this, "GitHub Connected",
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
            FlyoutHelper.ShowErrorFlyout(anchor ?? this, "Missing Configuration",
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
        await OnboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
        await OnboardingService.CompleteStep(3);

        // Update system status to reflect the changes
        UpdateSystemStatus();

        FlyoutHelper.ShowSuccessFlyout(anchor ?? this, "Git Server Connected",
            "Successfully connected to your Git server!");
    }

    // Flyout helper methods for less intrusive dialogs
    private void ShowConfirmationFlyout(FrameworkElement? anchor, string title, string message)
    {
        if (anchor == null) return;

        var okButton = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };

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
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(ColorConstants.SecondaryText),
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    okButton
                }
            },
            Placement = FlyoutPlacementMode.Right
        };

        okButton.Click += (_, _) => flyout.Hide();
        flyout.ShowAt(anchor);
    }

    private async Task<bool> ShowConfirmationFlyoutWithOk(FrameworkElement? anchor, string title, string message)
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
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(ColorConstants.SecondaryText),
                        Margin = new Thickness(0, 0, 0, 12)
                    },
                    okButton
                }
            },
            Placement = FlyoutPlacementMode.Right
        };

        okButton.Click += (_, _) =>
        {
            tcs.SetResult(true);
            flyout.Hide();
        };

        flyout.ShowAt(anchor);
        return await tcs.Task;
    }

    // Methods updated to use FlyoutHelper
}
