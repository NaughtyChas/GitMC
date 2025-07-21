using GitMC.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace GitMC.Views
{
    public partial class MainWindow : Window
    {
        private void InitializeTitleBar()
        {
            // Since this is now the actual Window, we can configure it directly
            this.Title = "GitMC - Minecraft Save Manager";
            this.ExtendsContentIntoTitleBar = false; // Show the title bar
        }

        public MainWindow()
        {
            this.InitializeComponent();
            this.InitializeTitleBar();
            // Navigate to the HomePage by default
            ContentFrame.Navigate(typeof(HomePage));
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
