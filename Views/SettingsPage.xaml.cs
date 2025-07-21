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
            // Navigate to the DebugPage
            // This assumes you have a Frame in your MainWindow to host pages.
            (Application.Current as App)?.RootFrame?.Navigate(typeof(DebugPage));
        }
    }
}
