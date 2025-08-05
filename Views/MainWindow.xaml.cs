using System.Diagnostics;
using GitMC.Extensions;
using GitMC.Models;
using GitMC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace GitMC.Views;

public sealed partial class MainWindow : Window
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly ManagedSaveService _managedSaveService;
    private readonly IOnboardingService _onboardingService;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services using ServiceFactory for consistency
        var services = ServiceFactory.Services;
        _dataStorageService = services.DataStorage;
        _configurationService = services.Configuration;
        _onboardingService = services.Onboarding;
        _managedSaveService = new ManagedSaveService(_dataStorageService);

        SetWindowProperties();
        _ = LoadExistingSavesToNavigationAsync(); // Load saved entries on startup
        NavigateToHomePage();
        ContentFrame.Navigated += ContentFrame_Navigated;

        // Only subscribe to window close event for saving configuration
        Closed += MainWindow_Closed;
    }

    private async void NavigateToHomePage()
    {
        try
        {
            // Initialize services first
            await _onboardingService.InitializeAsync();

            // Determine target page and navigate directly
            Type targetPageType = GetHomeTargetPageType();
            ContentFrame.Navigate(targetPageType);
        }
        catch
        {
            // Fallback to OnboardingPage if there's any error
            ContentFrame.Navigate(typeof(OnboardingPage));
        }
    }

    private Type GetHomeTargetPageType()
    {
        // Determine which page to show based on managed saves
        if (HasManagedSaves())
            return typeof(SaveManagementPage);
        return typeof(OnboardingPage);
    }

    private bool HasManagedSaves()
    {
        return GetManagedSavesCount() > 0;
    }

    private int GetManagedSavesCount()
    {
        try
        {
            // Use the ManagedSaveService to get the count
            return _managedSaveService.GetManagedSavesCount();
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
        return _managedSaveService.GetManagedSavesStoragePath();
    }

    private async void SetWindowProperties()
    {
        Title = "GitMC";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon("Assets/Icons/mcIcon.png");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        // Load configuration first
        await _configurationService.LoadAsync();

        // Define minimum window size (1366*720)
        const int minWidth = 1366;
        const int minHeight = 720;

        // Default window size (1520*800) - used for first launch
        const int defaultWidth = 1520;
        const int defaultHeight = 800;

        // Check if this is the first launch
        bool isFirstLaunch = !_configurationService.IsFirstLaunchComplete;

        if (isFirstLaunch)
        {
            // First launch: use default size and center the window
            AppWindow.Resize(new SizeInt32(defaultWidth, defaultHeight));

            // Center the window on screen
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                int centerX = (displayArea.WorkArea.Width - defaultWidth) / 2;
                int centerY = (displayArea.WorkArea.Height - defaultHeight) / 2;
                AppWindow.Move(new PointInt32(centerX, centerY));
            }

            // Mark first launch as complete
            _configurationService.IsFirstLaunchComplete = true;
            await _configurationService.SaveAsync();
        }
        else
        {
            // Restore saved window size and position
            int savedWidth = (int)Math.Max(_configurationService.WindowWidth, minWidth);
            int savedHeight = (int)Math.Max(_configurationService.WindowHeight, minHeight);
            int savedX = (int)_configurationService.WindowX;
            int savedY = (int)_configurationService.WindowY;

            AppWindow.Resize(new SizeInt32(savedWidth, savedHeight));
            AppWindow.Move(new PointInt32(savedX, savedY));

            // Restore maximized state if needed
            if (_configurationService.IsMaximized)
            {
                var presenter = AppWindow.Presenter as OverlappedPresenter;
                presenter?.Maximize();
            }
        }

        // Set up window size change monitoring to enforce minimum size
        AppWindow.Changed += AppWindow_Changed;
    }

    public void NavigateToPage(Type pageType)
    {
        ContentFrame.Navigate(pageType);
    }

    public void NavigateToSaveDetail(string saveId)
    {
        ContentFrame.Navigate(typeof(SaveDetailPage), saveId);
    }

    public void AddSaveToNavigation(string saveName, string saveId)
    {
        NavView.MenuItems.Add(new NavigationViewItem
        {
            Content = saveName,
            Icon = new SymbolIcon(Symbol.Folder),
            Tag = saveId
        });
    }

    private async Task LoadExistingSavesToNavigationAsync()
    {
        try
        {
            List<ManagedSaveInfo> managedSaves = await _managedSaveService.GetManagedSaves();
            foreach (ManagedSaveInfo save in managedSaves) AddSaveToNavigation(save.Name, save.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load existing saves to navigation: {ex.Message}");
        }
    }


    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage)) ContentFrame.Navigate(typeof(SettingsPage));
        }
        else
        {
            NavigationViewItemBase? item = args.InvokedItemContainer;
            if (item != null)
            {
                string? tag = item.Tag?.ToString();
                if (tag == "Home")
                {
                    // Get the target home page type and only navigate if different
                    Type targetPageType = GetHomeTargetPageType();
                    if (ContentFrame.CurrentSourcePageType != targetPageType) ContentFrame.Navigate(targetPageType);
                }
                else if (tag == "Console")
                {
                    if (ContentFrame.CurrentSourcePageType != typeof(ConsolePage))
                        ContentFrame.Navigate(typeof(ConsolePage));
                }
                else if (tag == "Settings")
                {
                    if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                        ContentFrame.Navigate(typeof(SettingsPage));
                }
                else if (!string.IsNullOrEmpty(tag))
                {
                    // Handle save-specific navigation - tag should be the save ID
                    NavigateToSaveDetail(tag);
                }
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage)) ContentFrame.Navigate(typeof(SettingsPage));
        }
        else
        {
            var selectedItem = args.SelectedItem as NavigationViewItem;
            if (selectedItem != null)
            {
                string? tag = selectedItem.Tag?.ToString();
                if (tag == "Home")
                {
                    // Get the target home page type and only navigate if different
                    Type targetPageType = GetHomeTargetPageType();
                    if (ContentFrame.CurrentSourcePageType != targetPageType) ContentFrame.Navigate(targetPageType);
                }
                else if (tag == "Console")
                {
                    if (ContentFrame.CurrentSourcePageType != typeof(ConsolePage))
                        ContentFrame.Navigate(typeof(ConsolePage));
                }
                else if (tag == "Settings")
                {
                    if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                        ContentFrame.Navigate(typeof(SettingsPage));
                }
                else if (!string.IsNullOrEmpty(tag))
                {
                    // Handle save-specific navigation - tag should be the save ID
                    NavigateToSaveDetail(tag);
                }
            }
        }
    }

    private void TitleBar_BackButtonClick(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // NavView.IsBackEnabled = ContentFrame.CanGoBack;
        UpdateNavigationSelection(e.SourcePageType, e.Parameter);
    }

    private void UpdateNavigationSelection(Type pageType, object? parameter = null)
    {
        if (pageType == typeof(HomePage))
        {
            foreach (object? item in NavView.MenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Home")
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
        }
        else if (pageType == typeof(OnboardingPage) || pageType == typeof(SaveManagementPage))
        {
            // When HomePage routes to OnboardingPage or SaveManagementPage,
            // keep Home selected to maintain navigation state
            foreach (object? item in NavView.MenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Home")
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
        }
        else if (pageType == typeof(SaveDetailPage) && parameter is string saveId)
        {
            // For SaveDetailPage, find the corresponding save navigation item by saveId
            foreach (object? item in NavView.MenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == saveId)
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
        }
        else if (pageType == typeof(ConsolePage))
        {
            foreach (object? item in NavView.FooterMenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Console")
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
        }
        else if (pageType == typeof(SettingsPage))
        {
            // Look for custom Settings item in FooterMenuItems
            foreach (object? item in NavView.FooterMenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Settings")
                {
                    NavView.SelectedItem = navItem;
                    return;
                }

            // Fallback to built-in settings item if custom one not found
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else if (pageType == typeof(DebugPage) || pageType == typeof(SaveTranslatorPage))
        {
            // For debug/tools pages, keep settings selected since they're accessed from settings
            // Look for custom Settings item in FooterMenuItems
            foreach (object? item in NavView.FooterMenuItems)
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Settings")
                {
                    NavView.SelectedItem = navItem;
                    return;
                }

            // Fallback to built-in settings item if custom one not found
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            NavView.SelectedItem = null;
        }
    }

    // Window event handlers for size and position management
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Enforce minimum window size
        const int minWidth = 1366;
        const int minHeight = 720;

        if (args.DidSizeChange)
        {
            SizeInt32 currentSize = sender.Size;
            bool needsResize = false;
            int newWidth = currentSize.Width;
            int newHeight = currentSize.Height;

            if (currentSize.Width < minWidth)
            {
                newWidth = minWidth;
                needsResize = true;
            }

            if (currentSize.Height < minHeight)
            {
                newHeight = minHeight;
                needsResize = true;
            }

            if (needsResize) sender.Resize(new SizeInt32(newWidth, newHeight));
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs e)
    {
        // Save current window state when closing
        try
        {
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                // Save maximized state
                _configurationService.IsMaximized = presenter.State == OverlappedPresenterState.Maximized;

                // Only save size and position if not maximized
                if (presenter.State != OverlappedPresenterState.Maximized)
                {
                    _configurationService.WindowWidth = AppWindow.Size.Width;
                    _configurationService.WindowHeight = AppWindow.Size.Height;
                    _configurationService.WindowX = AppWindow.Position.X;
                    _configurationService.WindowY = AppWindow.Position.Y;
                }
            }

            // Save configuration to file
            await _configurationService.SaveAsync();
        }
        catch
        {
            // Ignore save errors on close
        }
    }
}
