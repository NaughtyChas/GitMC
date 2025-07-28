using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml;
using GitMC.Services;

namespace GitMC.Views
{
    public sealed partial class SettingsPage : Page
    {
        private ILocalizationService? _localizationService;

        public SettingsPage()
        {
            this.InitializeComponent();
            _localizationService = (App.Current as App)?.LocalizationService;
            
            // Set initial language selection
            if (_localizationService != null)
            {
                var currentLanguage = _localizationService.CurrentLanguage;
                for (int i = 0; i < LanguageComboBox.Items.Count; i++)
                {
                    if (LanguageComboBox.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == currentLanguage)
                    {
                        LanguageComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem && 
                selectedItem.Tag is string languageCode && 
                _localizationService != null)
            {
                _localizationService.SetLanguage(languageCode);
                
                // Show restart notification
                _ = ShowRestartNotification();
            }
        }

        private async System.Threading.Tasks.Task ShowRestartNotification()
        {
            var dialog = new ContentDialog
            {
                Title = _localizationService?.GetLocalizedString("Settings_Language") ?? "Language",
                Content = _localizationService?.GetLocalizedString("Settings_Language_SelectDescription") ?? "Changes will take effect after restarting the app",
                CloseButtonText = _localizationService?.GetLocalizedString("Common_OK") ?? "OK",
                XamlRoot = this.XamlRoot
            };
            
            await dialog.ShowAsync();
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
