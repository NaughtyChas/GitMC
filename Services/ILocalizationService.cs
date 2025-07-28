using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;
using Windows.ApplicationModel.Resources;

namespace GitMC.Services
{
    public interface ILocalizationService : INotifyPropertyChanged
    {
        string GetLocalizedString(string key);
        string GetLocalizedString(string key, params object[] args);
        void SetLanguage(string languageCode);
        string CurrentLanguage { get; }
        string[] SupportedLanguages { get; }
        string GetLanguageDisplayName(string languageCode);
    }

    public class LocalizationService : ILocalizationService
    {
        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LocalizationService()
        {
            _resourceManager = new ResourceManager("GitMC.Resources.Strings.Resources", typeof(LocalizationService).Assembly);
            _currentCulture = CultureInfo.CurrentUICulture;
        }

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
                
                // Notify UI of language change
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Language"));
            }
            catch (Exception ex)
            {
                // Log error and fallback to English
                System.Diagnostics.Debug.WriteLine($"Failed to set language to {languageCode}: {ex.Message}");
                SetLanguage("en-US");
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
}
