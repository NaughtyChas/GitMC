using System.ComponentModel;

namespace GitMC.Services
{
    public interface IConfigurationService : INotifyPropertyChanged
    {
        // Basic configuration methods
        bool GetBool(string key, bool defaultValue = false);
        void SetBool(string key, bool value);
        string GetString(string key, string defaultValue = "");
        void SetString(string key, string value);
        int GetInt(string key, int defaultValue = 0);
        void SetInt(string key, int value);
        
        // Async operations
        Task LoadAsync();
        Task SaveAsync();
        
        // Specific configuration properties for easy access
        bool IsLanguageConfigured { get; set; }
        bool IsPlatformConfigured { get; set; }
        bool IsSaveAdded { get; set; }
        bool IsFirstLaunchComplete { get; set; }
        int CurrentOnboardingStep { get; set; }
        
        // Events for UI synchronization
        event EventHandler<string> ConfigurationChanged;
    }
}
