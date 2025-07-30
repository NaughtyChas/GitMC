using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GitMC.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitMC.Views
{
    public partial class MainWindow : Window
    {
        private readonly IDataStorageService _dataStorageService;
        private readonly IOnboardingService _onboardingService;
        private readonly IGitService _gitService;
        private readonly IConfigurationService _configurationService;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _dataStorageService = new DataStorageService();
            _gitService = new GitService();
            _configurationService = new ConfigurationService();
            _onboardingService = new OnboardingService(_gitService, _configurationService);

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
                var targetPageType = GetHomeTargetPageType();
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
            {
                return typeof(SaveManagementPage);
            }
            else
            {
                return typeof(OnboardingPage);
            }
        }

        private bool HasManagedSaves()
        {
            return GetManagedSavesCount() > 0;
        }

        private int GetManagedSavesCount()
        {
            try
            {
                // Check for managed saves metadata files
                var managedSavesPath = GetManagedSavesStoragePath();
                if (!Directory.Exists(managedSavesPath))
                {
                    return 0;
                }

                // Count JSON metadata files that represent managed saves
                var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
                return jsonFiles.Length;
            }
            catch
            {
                // If there's any error accessing the filesystem, fall back to onboarding check
                var statuses = _onboardingService.StepStatuses;
                if (statuses.Length > 4 && statuses[4] == OnboardingStepStatus.Completed)
                {
                    return 1; // At least one save exists based on onboarding
                }
                return 0;
            }
        }

        private string GetManagedSavesStoragePath()
        {
            return _dataStorageService.GetManagedSavesDirectory();
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

            // Define minimum window size (800x600)
            const int MinWidth = 800;
            const int MinHeight = 600;

            // Default window size (1000x700) - used for first launch
            const int DefaultWidth = 1000;
            const int DefaultHeight = 700;

            // Check if this is the first launch
            bool isFirstLaunch = !_configurationService.IsFirstLaunchComplete;

            if (isFirstLaunch)
            {
                // First launch: use default size and center the window
                AppWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWidth, DefaultHeight));

                // Center the window on screen
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centerX = (displayArea.WorkArea.Width - DefaultWidth) / 2;
                    var centerY = (displayArea.WorkArea.Height - DefaultHeight) / 2;
                    AppWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
                }

                // Mark first launch as complete
                _configurationService.IsFirstLaunchComplete = true;
                await _configurationService.SaveAsync();
            }
            else
            {
                // Restore saved window size and position
                var savedWidth = (int)Math.Max(_configurationService.WindowWidth, MinWidth);
                var savedHeight = (int)Math.Max(_configurationService.WindowHeight, MinHeight);
                var savedX = (int)_configurationService.WindowX;
                var savedY = (int)_configurationService.WindowY;

                AppWindow.Resize(new Windows.Graphics.SizeInt32(savedWidth, savedHeight));
                AppWindow.Move(new Windows.Graphics.PointInt32(savedX, savedY));

                // Restore maximized state if needed
                if (_configurationService.IsMaximized)
                {
                    var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
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

        public void AddSaveToNavigation(string saveName, string savePath)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = saveName,
                Icon = new SymbolIcon(Symbol.Folder),
                Tag = savePath
            });
        }

        private async Task LoadExistingSavesToNavigationAsync()
        {
            try
            {
                var managedSaves = await GetManagedSavesAsync();
                foreach (var save in managedSaves)
                {
                    AddSaveToNavigation(save.Name, save.OriginalPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load existing saves to navigation: {ex.Message}");
            }
        }

        private async Task<List<ManagedSaveInfo>> GetManagedSavesAsync()
        {
            var saves = new List<ManagedSaveInfo>();
            var managedSavesPath = GetManagedSavesStoragePath();

            if (!Directory.Exists(managedSavesPath))
            {
                return saves;
            }

            var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(jsonFile);
                    var saveInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedSaveInfo>(json);
                    if (saveInfo != null)
                    {
                        saves.Add(saveInfo);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse save info from {jsonFile}: {ex.Message}");
                }
            }

            return saves.OrderByDescending(s => s.LastModified).ToList();
        }


        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                {
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
            }
            else
            {
                var item = args.InvokedItemContainer;
                if (item != null)
                {
                    var tag = item.Tag?.ToString();
                    if (tag == "Home")
                    {
                        // Get the target home page type and only navigate if different
                        var targetPageType = GetHomeTargetPageType();
                        if (ContentFrame.CurrentSourcePageType != targetPageType)
                        {
                            ContentFrame.Navigate(targetPageType);
                        }
                    }
                    else if (tag == "Console")
                    {
                        if (ContentFrame.CurrentSourcePageType != typeof(ConsolePage))
                        {
                            ContentFrame.Navigate(typeof(ConsolePage));
                        }
                    }
                    else if (tag == "Settings")
                    {
                        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                        {
                            ContentFrame.Navigate(typeof(SettingsPage));
                        }
                    }
                    // Potentially handle save-specific navigation here
                }
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                {
                    ContentFrame.Navigate(typeof(SettingsPage));
                }
            }
            else
            {
                var selectedItem = args.SelectedItem as NavigationViewItem;
                if (selectedItem != null)
                {
                    var tag = selectedItem.Tag?.ToString();
                    if (tag == "Home")
                    {
                        // Get the target home page type and only navigate if different
                        var targetPageType = GetHomeTargetPageType();
                        if (ContentFrame.CurrentSourcePageType != targetPageType)
                        {
                            ContentFrame.Navigate(targetPageType);
                        }
                    }
                    else if (tag == "Console")
                    {
                        if (ContentFrame.CurrentSourcePageType != typeof(ConsolePage))
                        {
                            ContentFrame.Navigate(typeof(ConsolePage));
                        }
                    }
                    else if (tag == "Settings")
                    {
                        if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                        {
                            ContentFrame.Navigate(typeof(SettingsPage));
                        }
                    }
                    // Potentially handle save-specific navigation here
                }
            }
        }

        private void TitleBar_BackButtonClick(TitleBar sender, object args)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            // NavView.IsBackEnabled = ContentFrame.CanGoBack;
            // IsBackButtonEnabled 已通过 XAML 绑定到 ContentFrame.CanGoBack
            UpdateNavigationSelection(e.SourcePageType);
        }

        private void UpdateNavigationSelection(Type pageType)
        {
            if (pageType == typeof(HomePage))
            {
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Home")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            else if (pageType == typeof(OnboardingPage) || pageType == typeof(SaveManagementPage))
            {
                // When HomePage routes to OnboardingPage or SaveManagementPage,
                // keep Home selected to maintain navigation state
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Home")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            else if (pageType == typeof(ConsolePage))
            {
                foreach (var item in NavView.FooterMenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Console")
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
            else if (pageType == typeof(SettingsPage))
            {
                // Look for custom Settings item in FooterMenuItems
                foreach (var item in NavView.FooterMenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Settings")
                    {
                        NavView.SelectedItem = navItem;
                        return;
                    }
                }
                // Fallback to built-in settings item if custom one not found
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else if (pageType == typeof(DebugPage) || pageType == typeof(SaveTranslatorPage))
            {
                // For debug/tools pages, keep settings selected since they're accessed from settings
                // Look for custom Settings item in FooterMenuItems
                foreach (var item in NavView.FooterMenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "Settings")
                    {
                        NavView.SelectedItem = navItem;
                        return;
                    }
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
        private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            // Enforce minimum window size
            const int MinWidth = 1366;
            const int MinHeight = 720;

            if (args.DidSizeChange)
            {
                var currentSize = sender.Size;
                bool needsResize = false;
                int newWidth = currentSize.Width;
                int newHeight = currentSize.Height;

                if (currentSize.Width < MinWidth)
                {
                    newWidth = MinWidth;
                    needsResize = true;
                }

                if (currentSize.Height < MinHeight)
                {
                    newHeight = MinHeight;
                    needsResize = true;
                }

                if (needsResize)
                {
                    sender.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
                }
            }
        }

        private async void MainWindow_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs e)
        {
            // Save current window state when closing
            try
            {
                var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                if (presenter != null)
                {
                    // Save maximized state
                    _configurationService.IsMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;

                    // Only save size and position if not maximized
                    if (presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
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
}
