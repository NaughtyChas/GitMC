using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml;

namespace GitMC.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
        }

        private void DebugToolsButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the DebugPage using the MainWindow's navigation method
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(DebugPage));
            }
        }

        private void SaveTranslatorButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the SaveTranslatorPage using the MainWindow's navigation method
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToPage(typeof(SaveTranslatorPage));
            }
        }
    }
}
