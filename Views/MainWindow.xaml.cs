using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitMC.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            SetWindowProperties();
            ContentFrame.Navigate(typeof(HomePage));
            ContentFrame.Navigated += ContentFrame_Navigated;
        }

        private void SetWindowProperties()
        {
            this.Title = "GitMC";
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(this.TitleBar);
            this.AppWindow.SetIcon("Assets/StoreLogo.png");
            this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
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
                        if (ContentFrame.CurrentSourcePageType != typeof(HomePage))
                        {
                            ContentFrame.Navigate(typeof(HomePage));
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
                        if (ContentFrame.CurrentSourcePageType != typeof(HomePage))
                        {
                            ContentFrame.Navigate(typeof(HomePage));
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
            else if (pageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else if (pageType == typeof(DebugPage) || pageType == typeof(SaveTranslatorPage))
            {
                // For debug/tools pages, keep settings selected since they're accessed from settings
                NavView.SelectedItem = NavView.SettingsItem;
            }
            else
            {
                NavView.SelectedItem = null;
            }
        }
    }
}
