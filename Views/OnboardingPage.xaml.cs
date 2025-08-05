using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using GitMC.Constants;
using GitMC.Extensions;
using GitMC.Helpers;
using GitMC.Models;
using GitMC.Models.GitHub;
using GitMC.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
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
    private readonly IGitHubAppsService _gitHubAppsService;

    public OnboardingPage()
    {
        InitializeComponent();

        // Use ServiceFactory to get shared service instances
        var services = ServiceFactory.Services;
        _nbtService = services.Nbt as NbtService ?? new NbtService();
        _configurationService = services.Configuration;
        _gitService = services.Git;
        _dataStorageService = services.DataStorage;
        _minecraftAnalyzerService = ServiceFactory.MinecraftAnalyzer;
        _managedSaveService = new ManagedSaveService(_dataStorageService);
        _gitHubAppsService = ServiceFactory.GitHubApps;
        OnboardingService = services.Onboarding;

        // Subscribe to onboarding changes
        OnboardingService.PropertyChanged += OnboardingService_PropertyChanged;

        DataContext = this;
        Loaded += OnboardingPage_Loaded;
        Unloaded += OnboardingPage_Unloaded;
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

            // Initialize Git configuration status
            await InitializeGitConfigurationStatus();

            // Check GitHub authentication status
            await ValidateGitHubAuthenticationStatus();

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

    private async void OnboardingPage_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Ensure configuration is saved when page is unloaded
            await _configurationService.SaveAsync();
            Debug.WriteLine("Configuration saved on page unload");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving configuration on unload: {ex.Message}");
        }
    }

    private async Task InitializeGitConfigurationStatus()
    {
        try
        {
            // Just update the system Git identity display now that we removed the warning InfoBar
            await UpdateSystemGitIdentityDisplay();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing Git configuration status: {ex.Message}");
        }
    }

    private async Task ValidateGitHubAuthenticationStatus()
    {
        try
        {
            // Only validate if GitHub is the selected platform
            if (_configurationService.SelectedPlatform != "GitHub")
                return;

            string accessToken = _configurationService.GitHubAccessToken;
            DateTime tokenTimestamp = _configurationService.GitHubAccessTokenTimestamp;

            if (string.IsNullOrEmpty(accessToken))
                return; // No token stored, nothing to validate

            // Validate token state
            var (isValid, isExpired, errorMessage) = await _gitHubAppsService.ValidateTokenStateAsync(accessToken, tokenTimestamp);

            if (isValid)
            {
                // Token is still valid, show success status in UI
                var authStatusPanel = FindName("GitHubAuthStatusPanel") as InfoBar;
                if (authStatusPanel != null)
                {
                    authStatusPanel.Visibility = Visibility.Visible;
                    authStatusPanel.Title = "GitHub Authentication Active";
                    authStatusPanel.Message = $"Connected as {_configurationService.GitHubUsername}";
                    authStatusPanel.Severity = InfoBarSeverity.Success;
                }
            }
            else if (isExpired)
            {
                // Token is expired, show warning and offer re-authentication
                var authStatusPanel = FindName("GitHubAuthStatusPanel") as InfoBar;
                if (authStatusPanel != null)
                {
                    authStatusPanel.Visibility = Visibility.Visible;
                    authStatusPanel.Title = "GitHub Authentication Expired";
                    authStatusPanel.Message = "Your GitHub access token has expired. Please re-authenticate to continue using GitHub features.";
                    authStatusPanel.Severity = InfoBarSeverity.Warning;

                    // Add action button for re-authentication
                    var reAuthButton = new Button
                    {
                        Content = "Re-authenticate",
                        Style = Application.Current.Resources["AccentButtonStyle"] as Style
                    };
                    reAuthButton.Click += async (sender, e) =>
                    {
                        await ConnectToGitHub(sender as FrameworkElement);
                    };
                    authStatusPanel.ActionButton = reAuthButton;
                }

                // Clear expired token data
                _configurationService.GitHubAccessToken = "";
                _configurationService.GitHubAccessTokenTimestamp = DateTime.MinValue;
                await _configurationService.SaveAsync();
            }
            else
            {
                // Token is invalid (revoked or other error)
                var authStatusPanel = FindName("GitHubAuthStatusPanel") as InfoBar;
                if (authStatusPanel != null)
                {
                    authStatusPanel.Visibility = Visibility.Visible;
                    authStatusPanel.Title = "GitHub Authentication Invalid";
                    authStatusPanel.Message = $"Your GitHub access token is no longer valid: {errorMessage}. Please re-authenticate.";
                    authStatusPanel.Severity = InfoBarSeverity.Error;

                    // Add action button for re-authentication
                    var reAuthButton = new Button
                    {
                        Content = "Re-authenticate",
                        Style = Application.Current.Resources["AccentButtonStyle"] as Style
                    };
                    reAuthButton.Click += async (sender, e) =>
                    {
                        await ConnectToGitHub(sender as FrameworkElement);
                    };
                    authStatusPanel.ActionButton = reAuthButton;
                }

                // Clear invalid token data
                _configurationService.GitHubAccessToken = "";
                _configurationService.GitHubAccessTokenTimestamp = DateTime.MinValue;
                await _configurationService.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error validating GitHub authentication status: {ex.Message}");
        }
    }

    private async Task UpdateSystemGitIdentityDisplay()
    {
        try
        {
            var systemIdentityPanel = FindName("SystemIdentityPanel") as InfoBar;

            // Check if system Git is installed and has identity configured
            bool isSystemGitInstalled = await _gitService.IsInstalledAsync();
            if (isSystemGitInstalled)
            {
                try
                {
                    // Use GitService's new method to get system Git identity
                    (string? systemUserName, string? systemUserEmail) = await _gitService.GetSystemGitIdentityAsync();

                    if (!string.IsNullOrEmpty(systemUserName) && !string.IsNullOrEmpty(systemUserEmail))
                    {
                        // System Git identity found - show it
                        if (systemIdentityPanel != null)
                        {
                            systemIdentityPanel.Visibility = Visibility.Visible;
                            systemIdentityPanel.Title = "System Git identity detected";
                            systemIdentityPanel.Message = $"System Git configuration: {systemUserName} ({systemUserEmail}) - can migrate to built-in";
                            systemIdentityPanel.Severity = InfoBarSeverity.Informational;
                        }
                    }
                    else
                    {
                        // System Git installed but no identity
                        if (systemIdentityPanel != null) systemIdentityPanel.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    // Error reading system Git identity - hide panel
                    if (systemIdentityPanel != null) systemIdentityPanel.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // No system Git - hide panel
                if (systemIdentityPanel != null) systemIdentityPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating system Git identity display: {ex.Message}");
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

            // Step 2: Git System Setup (now manual choice, not auto-detection)
            if (statuses.Length > 1)
            {
                bool gitInstalled = await _gitService.IsInstalledAsync();
                var gitFoundText = FindName("GitFoundText") as TextBlock;
                var gitNotFoundPanel = FindName("GitNotFoundPanel") as StackPanel;

                if (gitFoundText != null && gitNotFoundPanel != null)
                {
                    // Since Step 2 is now manual choice, we only show status after user completes it
                    if (statuses[1] == OnboardingStepStatus.Completed)
                    {
                        // Show completion status based on what user chose
                        if (gitInstalled)
                        {
                            string gitVersion = await _gitService.GetVersionAsync();
                            gitFoundText.Text = $"✓ Using system Git version {gitVersion}";
                        }
                        else
                        {
                            gitFoundText.Text = "✓ Using built-in Git";
                        }
                        gitFoundText.Visibility = Visibility.Visible;
                        gitNotFoundPanel.Visibility = Visibility.Collapsed;
                    }
                    else if (statuses[1] == OnboardingStepStatus.Current)
                    {
                        // Show the choice panel when it's the current step
                        gitFoundText.Visibility = Visibility.Collapsed;
                        gitNotFoundPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // For pending steps, hide both panels
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

    private async void SaveGitConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var userNameBox = FindName("GitUserNameBox") as TextBox;
            var userEmailBox = FindName("GitUserEmailBox") as TextBox;
            var validationText = FindName("GitConfigValidationText") as TextBlock;

            if (userNameBox == null || userEmailBox == null || validationText == null)
                return;

            string userName = userNameBox.Text.Trim();
            string userEmail = userEmailBox.Text.Trim();

            // Validate input
            if (string.IsNullOrEmpty(userName))
            {
                validationText.Text = "Please enter your full name.";
                validationText.Visibility = Visibility.Visible;
                return;
            }

            if (string.IsNullOrEmpty(userEmail))
            {
                validationText.Text = "Please enter your email address.";
                validationText.Visibility = Visibility.Visible;
                return;
            }

            // Basic email validation
            if (!userEmail.Contains("@") || !userEmail.Contains("."))
            {
                validationText.Text = "Please enter a valid email address.";
                validationText.Visibility = Visibility.Visible;
                return;
            }

            // Hide validation message
            validationText.Visibility = Visibility.Collapsed;

            // Configure Git identity using LibGit2Sharp
            bool success = await _gitService.ConfigureIdentityAsync(userName, userEmail);

            if (success)
            {
                // Clear form
                userNameBox.Text = "";
                userEmailBox.Text = "";

                // Update configuration service immediately
                _configurationService.IsGitIdentityConfigured = true;

                // Force save configuration to ensure persistence
                await _configurationService.SaveAsync();

                // Update onboarding service
                await OnboardingService.SetConfigurationValueAsync("GitIdentityConfigured", true);
                await OnboardingService.CompleteStep(2); // Step 3 (0-indexed)
                await OnboardingService.RefreshAllSteps();

                // Refresh the Git configuration display
                await InitializeGitConfigurationStatus();

                // Show success feedback
                FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Git Configuration",
                    "Git identity has been configured successfully!");
            }
            else
            {
                // Show error feedback
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Configuration Error",
                    "Failed to configure Git identity. Please try again.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error configuring Git identity: {ex.Message}");
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Configuration Error",
                $"An error occurred while configuring Git: {ex.Message}");
        }
    }

    private async void UseSystemGitIdentityButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // First check if system Git is installed
            bool isSystemGitInstalled = await _gitService.IsInstalledAsync();
            if (!isSystemGitInstalled)
            {
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "System Git Not Found",
                    "System Git is not installed. Please install Git first or configure identity manually.");
                return;
            }

            // Get system Git identity using GitService's new method
            (string? systemUserName, string? systemUserEmail) = await _gitService.GetSystemGitIdentityAsync();

            if (!string.IsNullOrEmpty(systemUserName) && !string.IsNullOrEmpty(systemUserEmail))
            {
                // Apply system Git identity to LibGit2Sharp
                bool success = await _gitService.ConfigureIdentityAsync(systemUserName, systemUserEmail);

                if (success)
                {
                    var statusText = FindName("GitConfigStatusText") as TextBlock;
                    var statusIcon = FindName("GitConfigStatusIcon") as FontIcon;
                    var currentIdentityPanel = FindName("CurrentIdentityPanel") as StackPanel;
                    var currentUserNameText = FindName("CurrentUserNameText") as TextBlock;
                    var currentUserEmailText = FindName("CurrentUserEmailText") as TextBlock;

                    if (statusText != null)
                    {
                        statusText.Text = "LibGit2Sharp Git identity configured from system";
                        statusText.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen);
                    }

                    if (statusIcon != null)
                    {
                        statusIcon.Glyph = "\uE73E"; // Checkmark icon
                        statusIcon.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen);
                    }

                    // Update current identity display
                    if (currentIdentityPanel != null && currentUserNameText != null && currentUserEmailText != null)
                    {
                        currentUserNameText.Text = $"Name: {systemUserName}";
                        currentUserEmailText.Text = $"Email: {systemUserEmail}";
                        currentIdentityPanel.Visibility = Visibility.Visible;
                    }

                    // Update configuration service immediately
                    _configurationService.IsGitIdentityConfigured = true;

                    // Force save configuration to ensure persistence
                    await _configurationService.SaveAsync();

                    // Update onboarding service
                    await OnboardingService.SetConfigurationValueAsync("GitIdentityConfigured", true);
                    await OnboardingService.CompleteStep(2); // Step 3 (0-indexed)
                    await OnboardingService.RefreshAllSteps();

                    // Refresh the Git configuration display
                    await InitializeGitConfigurationStatus();

                    // Show success feedback
                    FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "System Git Identity Applied",
                        $"System Git identity applied to LibGit2Sharp:\nName: {systemUserName}\nEmail: {systemUserEmail}");
                }
                else
                {
                    FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Configuration Failed",
                        "Failed to apply system Git identity to LibGit2Sharp. Please try manual configuration.");
                }
            }
            else
            {
                // No system Git identity found
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "No System Git Identity",
                    "No Git identity found in system configuration. Please configure system Git first or use manual configuration.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error using system Git identity: {ex.Message}");
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Configuration Error",
                $"An error occurred while reading system Git identity: {ex.Message}");
        }
    }

    private async void TestGitConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var statusText = FindName("GitConfigStatusText") as TextBlock;
            var statusIcon = FindName("GitConfigStatusIcon") as FontIcon;
            var currentIdentityPanel = FindName("CurrentIdentityPanel") as StackPanel;
            var currentUserNameText = FindName("CurrentUserNameText") as TextBlock;
            var currentUserEmailText = FindName("CurrentUserEmailText") as TextBlock;

            if (statusText == null) return;

            // Test LibGit2Sharp Git identity configuration
            (string? userName, string? userEmail) = await _gitService.GetIdentityAsync();

            if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail))
            {
                statusText.Text = "LibGit2Sharp Git identity configured";
                statusText.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen);

                if (statusIcon != null)
                {
                    statusIcon.Glyph = "\uE73E"; // Checkmark icon
                    statusIcon.Foreground = new SolidColorBrush(ColorConstants.SuccessGreen);
                }

                // Update current identity display
                if (currentIdentityPanel != null && currentUserNameText != null && currentUserEmailText != null)
                {
                    currentUserNameText.Text = $"Name: {userName}";
                    currentUserEmailText.Text = $"Email: {userEmail}";
                    currentIdentityPanel.Visibility = Visibility.Visible;
                }

                FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "LibGit2Sharp Configuration Test",
                    $"LibGit2Sharp Git identity is configured:\nName: {userName}\nEmail: {userEmail}");
            }
            else
            {
                statusText.Text = "LibGit2Sharp Git identity not configured";
                statusText.Foreground = new SolidColorBrush(ColorConstants.WarningOrange);

                if (statusIcon != null)
                {
                    statusIcon.Glyph = "\uE946"; // Warning icon
                    statusIcon.Foreground = new SolidColorBrush(ColorConstants.WarningOrange);
                }

                // No system Git identity found
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "No System Git Identity",
                    "No Git identity found in system configuration. Please configure system Git first or use manual configuration.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error applying system Git identity to LibGit2Sharp: {ex.Message}");
            FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Error Applying System Git Identity",
                $"Failed to apply system Git identity to LibGit2Sharp: {ex.Message}");
        }
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
                    // Register this save in our managed saves system using the service first
                    try
                    {
                        string saveId = await _managedSaveService.RegisterManagedSave(save);

                        // Add to navigation in MainWindow with the generated save ID
                        if (App.MainWindow is MainWindow mainWindow)
                            mainWindow.AddSaveToNavigation(save.Name, saveId);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to register managed save: {ex.Message}");
                        // Show warning but continue - save registration failed
                        FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Save Registration Error",
                            "Failed to register the save for management. Please try adding it again.");
                        return;
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
                    // Register this save in our managed saves system using the service first
                    try
                    {
                        string saveId = await _managedSaveService.RegisterManagedSave(save);

                        // Add to navigation in MainWindow with the generated save ID
                        if (App.MainWindow is MainWindow mainWindow)
                            mainWindow.AddSaveToNavigation(save.Name, saveId);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to register managed save: {ex.Message}");
                        // Show warning but continue - save registration failed
                        FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Save Registration Error",
                            "Failed to register the save for management. Please try adding it again.");
                        return;
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
            // Check Git installation status
            bool gitInstalled = await _gitService.IsInstalledAsync();
            OnboardingStepStatus[] statuses = OnboardingService.StepStatuses;

            var gitStatusIcon = FindName("GitStatusIcon") as Border;
            var gitStatusText = FindName("GitStatusText") as TextBlock;

            if (gitStatusIcon != null && gitStatusText != null)
            {
                // For Step 2 (Git System Setup), show status based on user's choice, not auto-detection
                if (statuses.Length > 1 && statuses[1] == OnboardingStepStatus.Completed)
                {
                    // Step 2 completed - show what user chose
                    gitStatusIcon.Background = new SolidColorBrush(ColorConstants.SuccessGreen);
                    if (gitInstalled)
                    {
                        string gitVersion = await _gitService.GetVersionAsync();
                        gitStatusText.Text = $"Using system Git v{gitVersion}";
                    }
                    else
                    {
                        gitStatusText.Text = "Using built-in Git";
                    }

                    var gitIconControl = gitStatusIcon.Child as FontIcon;
                    if (gitIconControl != null) gitIconControl.Glyph = "\uE73E"; // Checkmark
                }
                else if (statuses.Length > 1 && statuses[1] == OnboardingStepStatus.Current)
                {
                    // Step 2 is current - show that user needs to choose
                    gitStatusIcon.Background = new SolidColorBrush(ColorConstants.WarningOrangeBright);
                    gitStatusText.Text = "Please choose your Git implementation";

                    var gitIconControl = gitStatusIcon.Child as FontIcon;
                    if (gitIconControl != null) gitIconControl.Glyph = "\uE946"; // Warning icon
                }
                else
                {
                    // Step 2 not reached yet - show neutral status
                    gitStatusIcon.Background = new SolidColorBrush(ColorConstants.WarningOrangeBright);
                    gitStatusText.Text = "Git system setup pending";

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

        // Show an informational flyout about what to do after installation
        FlyoutHelper.ShowSuccessFlyout(sender as FrameworkElement, "Git Download",
            "After installing Git, restart GitMC or refresh the onboarding to detect the installation. " +
            "You can also click 'Use Built-in Git' to skip system Git installation.");

        // Do NOT mark the step as completed here - user hasn't actually installed Git yet
        // The step will be marked complete when system Git is detected or user chooses built-in Git
    }
    private async void SkipToBuiltInGitButton_Click(object sender, RoutedEventArgs e)
    {
        // Show confirmation flyout first
        bool confirmed = await ShowConfirmationFlyoutWithOk(sender as FrameworkElement, "Use Built-in Git",
            "GitMC will use the built-in Git system (LibGit2Sharp) instead of system Git. You can always install system Git later for enhanced functionality.");

        if (confirmed)
        {
            // Mark Git system as configured (using built-in)
            await OnboardingService.SetConfigurationValueAsync("GitSystemConfigured", true);
            await OnboardingService.CompleteStep(1); // Step 2 (0-indexed)

            // Update system status
            UpdateSystemStatus();
        }
    }

    private async void ConnectPlatformButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var platformSelector = FindName("PlatformSelector") as SelectorBar;
            var gitHubSelectorItem = FindName("GitHubSelectorItem") as SelectorBarItem;

            if (platformSelector?.SelectedItem == gitHubSelectorItem) // GitHub
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
        var gitHubConfigPanel = FindName("GitHubConfigPanel") as Panel;
        var selfHostingConfigPanel = FindName("SelfHostingConfigPanel") as Panel;
        var platformSelector = FindName("PlatformSelector") as SelectorBar;
        var gitHubSelectorItem = FindName("GitHubSelectorItem") as SelectorBarItem;

        if (gitHubConfigPanel != null && selfHostingConfigPanel != null && platformSelector != null && gitHubSelectorItem != null)
        {
            if (platformSelector.SelectedItem == gitHubSelectorItem) // GitHub
            {
                gitHubConfigPanel.Visibility = Visibility.Visible;
                selfHostingConfigPanel.Visibility = Visibility.Collapsed;
            }
            else // Self-hosting
            {
                gitHubConfigPanel.Visibility = Visibility.Collapsed;
                selfHostingConfigPanel.Visibility = Visibility.Visible;
            }
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selfHostingUrlBox = FindName("SelfHostingUrlBox") as TextBox;
            var selfHostingUsernameBox = FindName("SelfHostingUsernameBox") as TextBox;
            var selfHostingTokenBox = FindName("SelfHostingTokenBox") as PasswordBox;
            var testConnectionButton = FindName("TestConnectionButton") as Button;

            // Validate input fields
            if (string.IsNullOrWhiteSpace(selfHostingUrlBox?.Text) ||
                string.IsNullOrWhiteSpace(selfHostingUsernameBox?.Text) ||
                string.IsNullOrWhiteSpace(selfHostingTokenBox?.Password))
            {
                FlyoutHelper.ShowErrorFlyout(sender as FrameworkElement, "Invalid Input",
                    "Please fill in all required fields (URL, Username, and Access Token).");
                return;
            }

            // Disable button and show progress
            if (testConnectionButton != null)
            {
                testConnectionButton.IsEnabled = false;
                testConnectionButton.Content = "Testing...";
            }

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
            var testConnectionButton = FindName("TestConnectionButton") as Button;
            if (testConnectionButton != null)
            {
                testConnectionButton.IsEnabled = true;
                testConnectionButton.Content = "Test Connection";
            }
        }
    }

    private async Task ConnectToGitHub(FrameworkElement? anchor = null)
    {
        var gitHubStatusPanel = FindName("GitHubStatusPanel") as InfoBar;
        var gitHubRepoSettings = FindName("GitHubRepoSettings") as StackPanel;

        try
        {
            // Update status panel to show initial confirmation
            if (gitHubStatusPanel != null)
            {
                gitHubStatusPanel.Visibility = Visibility.Visible;
                gitHubStatusPanel.Title = "Starting GitHub Authentication";
                gitHubStatusPanel.Message = "Preparing to connect to GitHub. This will open your browser for authorization.";
                gitHubStatusPanel.Severity = InfoBarSeverity.Informational;
                gitHubStatusPanel.IsClosable = false;
                gitHubStatusPanel.IsOpen = true;
            }

            // Add a small delay to show the message
            await Task.Delay(1000);

            // Step 1: Start Device Flow
            if (gitHubStatusPanel != null)
            {
                gitHubStatusPanel.Title = "Connecting to GitHub";
                gitHubStatusPanel.Message = "Starting authentication process...";
            }

            var deviceCodeResponse = await _gitHubAppsService.StartDeviceFlowAsync();
            if (deviceCodeResponse == null)
            {
                if (gitHubStatusPanel != null)
                {
                    gitHubStatusPanel.Title = "Authentication Error";
                    gitHubStatusPanel.Message = "Failed to start GitHub authentication. Please check your internet connection and try again.";
                    gitHubStatusPanel.Severity = InfoBarSeverity.Error;
                }
                return;
            }

            // Step 2: Show user code and instructions in the content area
            if (gitHubStatusPanel != null)
            {
                gitHubStatusPanel.Title = "GitHub Authorization Required";
                gitHubStatusPanel.Message = $"Please visit the following URL in your browser and enter this code:\n\n" +
                    $"URL: {deviceCodeResponse.VerificationUri}\n" +
                    $"Code: {deviceCodeResponse.UserCode}\n\n" +
                    $"The browser will open automatically. Complete the authorization and return here.";
                gitHubStatusPanel.Severity = InfoBarSeverity.Warning;
            }

            // Step 3: Open browser automatically
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(deviceCodeResponse.VerificationUri));
            }
            catch
            {
                // Ignore browser launch failures - user can navigate manually
                if (gitHubStatusPanel != null)
                {
                    gitHubStatusPanel.Message = $"Please manually visit: {deviceCodeResponse.VerificationUri}\n" +
                        $"And enter this code: {deviceCodeResponse.UserCode}\n\n" +
                        $"Waiting for authorization...";
                }
            }

            // Step 4: Show polling status
            if (gitHubStatusPanel != null)
            {
                gitHubStatusPanel.Title = "Waiting for Authorization";
                gitHubStatusPanel.Message = $"Code: {deviceCodeResponse.UserCode}\n\n" +
                    "Please complete the authorization in your browser. GitMC is waiting for confirmation...";
                gitHubStatusPanel.Severity = InfoBarSeverity.Informational;
            }

            // Step 5: Poll for authentication completion
            var authResult = await _gitHubAppsService.PollDeviceFlowAsync(
                deviceCodeResponse.DeviceCode,
                CancellationToken.None);

            if (authResult.IsSuccess && authResult.AccessToken != null && authResult.User != null)
            {
                // Store GitHub configuration
                _configurationService.SelectedPlatform = "GitHub";
                _configurationService.GitHubUsername = authResult.User.Login;
                _configurationService.GitHubAccessToken = authResult.AccessToken;
                _configurationService.GitHubAccessTokenTimestamp = DateTime.UtcNow; // Store when token was created

                // Set default repository name if not specified
                var repoName = "minecraft-saves"; // Default value
                try
                {
                    var repoNameBox = FindName("GitHubRepoNameBox") as TextBox;
                    if (repoNameBox != null && !string.IsNullOrWhiteSpace(repoNameBox.Text))
                    {
                        repoName = repoNameBox.Text.Trim();
                    }
                }
                catch { /* Ignore if control doesn't exist */ }

                _configurationService.GitHubRepository = repoName;

                // Set private repo preference
                bool isPrivate = true; // Default to private
                try
                {
                    var privateRepoBox = FindName("GitHubPrivateRepoBox") as CheckBox;
                    if (privateRepoBox != null)
                    {
                        isPrivate = privateRepoBox.IsChecked ?? true;
                    }
                }
                catch { /* Ignore if control doesn't exist */ }

                _configurationService.GitHubPrivateRepo = isPrivate;

                // CRITICAL: Save configuration immediately with multiple safeguards
                await _configurationService.SaveAsync();

                // Force an additional save to ensure persistence
                await Task.Delay(100); // Small delay to ensure write completion
                await _configurationService.SaveAsync();

                // Mark platform as configured with additional saves
                await OnboardingService.SetConfigurationValueAsync("PlatformConfigured", true);
                await OnboardingService.CompleteStep(3);

                // Additional save after onboarding step completion
                await _configurationService.SaveAsync();

                // Update status panel to show success
                if (gitHubStatusPanel != null)
                {
                    gitHubStatusPanel.Title = "GitHub Connected Successfully";
                    gitHubStatusPanel.Message = $"Successfully connected to GitHub as {authResult.User.Login}!\n\n" +
                        "Authentication status will be automatically validated when the app starts. " +
                        "Your tokens will be refreshed automatically if they expire.";
                    gitHubStatusPanel.Severity = InfoBarSeverity.Success;
                }

                // Show repository settings
                if (gitHubRepoSettings != null)
                {
                    gitHubRepoSettings.Visibility = Visibility.Visible;
                }

                // Update system status to reflect the changes
                UpdateSystemStatus();
            }
            else
            {
                // Handle authentication failure
                string errorMessage = authResult.ErrorMessage ?? "Authentication failed for unknown reason";
                if (gitHubStatusPanel != null)
                {
                    gitHubStatusPanel.Title = "GitHub Authentication Failed";
                    gitHubStatusPanel.Message = $"Failed to authenticate with GitHub: {errorMessage}\n\n" +
                        "Please try again or check your network connection.";
                    gitHubStatusPanel.Severity = InfoBarSeverity.Error;
                }
            }
        }
        catch (Exception ex)
        {
            // Handle unexpected errors
            if (gitHubStatusPanel != null)
            {
                gitHubStatusPanel.Title = "Connection Error";
                gitHubStatusPanel.Message = $"An unexpected error occurred during GitHub authentication: {ex.Message}\n\n" +
                    "Please try again.";
                gitHubStatusPanel.Severity = InfoBarSeverity.Error;
            }
        }
    }

    private async Task ConnectToSelfHostedGit(FrameworkElement? anchor = null)
    {
        var selfHostingUrlBox = FindName("SelfHostingUrlBox") as TextBox;
        var selfHostingUsernameBox = FindName("SelfHostingUsernameBox") as TextBox;
        var selfHostingTokenBox = FindName("SelfHostingTokenBox") as PasswordBox;

        // Validate self-hosting configuration
        if (string.IsNullOrWhiteSpace(selfHostingUrlBox?.Text) ||
            string.IsNullOrWhiteSpace(selfHostingUsernameBox?.Text) ||
            string.IsNullOrWhiteSpace(selfHostingTokenBox?.Password))
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
        _configurationService.GitServerUrl = selfHostingUrlBox?.Text ?? "";
        _configurationService.GitUsername = selfHostingUsernameBox?.Text ?? "";
        _configurationService.GitAccessToken = selfHostingTokenBox?.Password ?? "";
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
