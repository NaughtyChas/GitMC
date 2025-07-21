using GitMC.Views;
using Microsoft.UI.Xaml.Controls;
using System;

namespace GitMC.Views
{
    public partial class MainWindow : Page
    {
        private void InitializeTitleBar()
        {
            Window? window = App.MainWindow;
            if (window != null) window.ExtendsContentIntoTitleBar = true;
        }

        public MainWindow()
        {
            this.InitializeComponent();
            this.InitializeTitleBar();
            // Navigate to the HomePage by default
            ContentFrame.Navigate(typeof(HomePage));
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
