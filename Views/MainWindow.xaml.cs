using Microsoft.UI.Windowing;

namespace GitMC.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            SetWindowProperties();
            ContentFrame.Navigate(typeof(HomePage));
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
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                // Handle other navigation items if necessary
                var item = args.InvokedItemContainer;
                if (item != null)
                {
                    var tag = item.Tag?.ToString();
                    if (tag == "Home")
                    {
                        ContentFrame.Navigate(typeof(HomePage));
                    }
                    // Potentially handle save-specific navigation here
                }
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else
            {
                var selectedItem = args.SelectedItem as NavigationViewItem;
                if (selectedItem != null)
                {
                    var tag = selectedItem.Tag?.ToString();
                    if (tag == "Home")
                    {
                        ContentFrame.Navigate(typeof(HomePage));
                    }
                    // Potentially handle save-specific navigation here
                }
            }
        }
    }
}
