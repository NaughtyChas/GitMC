using GitMC.Services;

namespace GitMC.Views
{
    public sealed partial class SettingsPage : Page
    {
        private ILocalizationService? _localizationService;

        public SettingsPage()
        {
            InitializeComponent();
            _localizationService = (Application.Current as App)?.LocalizationService;
            
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
            }
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
