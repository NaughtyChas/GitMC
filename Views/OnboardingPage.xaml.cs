using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using GitMC.Constants;
using GitMC.Helpers;
using GitMC.Models;
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

    public OnboardingPage()
    {
        InitializeComponent();
        _nbtService = new NbtService();
        _configurationService = new ConfigurationService();
        _gitService = new GitService(_configurationService);
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

            // Initialize Git configuration status
            await InitializeGitConfigurationStatus();

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
            GitHubStatusPanel.Message = $"Connected as {_configurationService.GitHubUsername}";
            GitHubStatusPanel.Severity = InfoBarSeverity.Success;
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
