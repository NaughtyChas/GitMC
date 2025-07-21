using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitMC.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
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
                if (App.MainWindow?.Content is Frame rootFrame && rootFrame.Content is MainWindow mainWindow)
                {
                    (mainWindow.FindName("NavView") as NavigationView)?.MenuItems.Add(new NavigationViewItem
                    {
                        Content = folder.Name,
                        Icon = new SymbolIcon(Symbol.Folder),
                        Tag = folder.Path
                    });
                }
            }
        }
    }
}
