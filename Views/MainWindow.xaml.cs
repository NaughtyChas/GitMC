using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitMC.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainWindow : Page
    {
        public MainWindow()
        {
            this.InitializeComponent();
        }

        private async void SelectSaveButton_Click(object sender, RoutedEventArgs e)
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
                // For now, let's just add it to the navigation view
                NavView.MenuItems.Add(new NavigationViewItem
                {
                    Content = folder.Name,
                    Icon = new SymbolIcon(Symbol.Folder),
                    Tag = folder.Path
                });
            }
        }
    }
}
