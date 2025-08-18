using GitMC.Services;
using GitMC.Extensions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitMC.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ILocalizationService? _localizationService;
    private readonly IConfigurationService _configurationService;

    public SettingsPage()
    {
        InitializeComponent();
        _localizationService = (Application.Current as App)?.LocalizationService;
        _configurationService = ServiceFactory.Services.Configuration;

        // Set initial language selection
        if (_localizationService != null)
        {
            var currentLanguage = _localizationService.CurrentLanguage;
            for (var i = 0; i < LanguageComboBox.Items.Count; i++)
                if (LanguageComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == currentLanguage)
                {
                    LanguageComboBox.SelectedIndex = i;
                    break;
                }
        }

        // Set initial theme selection
        var currentTheme = _configurationService.Theme;
        for (var i = 0; i < ThemeComboBox.Items.Count; i++)
            if (ThemeComboBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == currentTheme)
            {
                ThemeComboBox.SelectedIndex = i;
                break;
            }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string languageCode &&
            _localizationService != null)
            _localizationService.SetLanguage(languageCode);
    }

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string theme)
        {
            // Save theme to configuration
            _configurationService.Theme = theme;
            await _configurationService.SaveAsync();

            // Apply theme immediately
            ApplyTheme(theme);
        }
    }

    private void ApplyTheme(string theme)
    {
        // Apply theme through MainWindow
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.ApplyTheme(theme);
        }
    }

    private void DebugToolsButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to the DebugPage using the MainWindow's navigation method
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(DebugPage));
    }

    private void SaveTranslatorButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to the SaveTranslatorPage using the MainWindow's navigation method
        if (App.MainWindow is MainWindow mainWindow) mainWindow.NavigateToPage(typeof(SaveTranslatorPage));
    }
}