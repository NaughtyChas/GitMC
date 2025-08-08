using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;

namespace GitMC.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }
    string[] SupportedLanguages { get; }
    string GetLocalizedString(string key);
    string GetLocalizedString(string key, params object[] args);
    void SetLanguage(string languageCode);
    string GetLanguageDisplayName(string languageCode);
}

public class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _resourceManager =
            new ResourceManager("GitMC.Resources.Strings.Resources", typeof(LocalizationService).Assembly);
        _currentCulture = new CultureInfo("en-US"); // Default to English
    }

    // Indexer property for binding support
    public string this[string key] => GetLocalizedString(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GetLocalizedString(string key)
    {
        try
        {
            var value = _resourceManager.GetString(key, _currentCulture);
            return value ?? key; // Return key if localization not found
        }
        catch
        {
            return key; // Fallback to key
        }
    }

    public string GetLocalizedString(string key, params object[] args)
    {
        var format = GetLocalizedString(key);
        try
        {
            return string.Format(format, args);
        }
        catch
        {
            return format; // Return unformatted string if formatting fails
        }
    }

    public void SetLanguage(string languageCode)
    {
        try
        {
            var culture = new CultureInfo(languageCode);
            _currentCulture = culture;

            // Set thread culture for proper resource loading
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;

            // Notify UI of language change - use Item[] to update all localized strings
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Language"));
        }
        catch (Exception ex)
        {
            // Log error and fallback to English
            Debug.WriteLine($"Failed to set language to {languageCode}: {ex.Message}");
            if (languageCode != "en-US") SetLanguage("en-US");
        }
    }

    public string CurrentLanguage => _currentCulture.Name;

    public string[] SupportedLanguages => new[] { "en-US", "zh-CN" };

    public string GetLanguageDisplayName(string languageCode)
    {
        return languageCode switch
        {
            "en-US" => "English",
            "zh-CN" => "简体中文",
            _ => languageCode
        };
    }
}