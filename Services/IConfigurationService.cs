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
        
        // Onboarding properties
        bool IsLanguageConfigured { get; set; }
        bool IsPlatformConfigured { get; set; }
        bool IsSaveAdded { get; set; }
        bool IsFirstLaunchComplete { get; set; }
        int CurrentOnboardingStep { get; set; }
        
        // Application preferences
        string CurrentLanguage { get; set; }
        string Theme { get; set; }
        
        // Window state and layout
        double WindowWidth { get; set; }
        double WindowHeight { get; set; }
        double WindowX { get; set; }
        double WindowY { get; set; }
        bool IsMaximized { get; set; }
        
        // Git configuration
        string LastUsedGitPath { get; set; }
        string DefaultGitUserName { get; set; }
        string DefaultGitUserEmail { get; set; }
        
        // Repository and save management
        string LastOpenedSavePath { get; set; }
        string[] RecentSaves { get; set; }
        int MaxRecentSaves { get; set; }
        
        // Platform settings
        string SelectedPlatform { get; set; } // "GitHub", "GitLab", "Local", etc.
        string PlatformToken { get; set; }
        string PlatformUsername { get; set; }
        string PlatformEmail { get; set; }
        
        // GitHub specific settings
        string GitHubAccessToken { get; set; }
        string GitHubUsername { get; set; }
        string GitHubRepository { get; set; }
        bool GitHubPrivateRepo { get; set; }
        
        // Git server settings (for self-hosted)
        string GitServerUrl { get; set; }
        string GitUsername { get; set; }
        string GitAccessToken { get; set; }
        string GitServerType { get; set; } // "GitLab", "Gitea", "Custom", etc.
        
        // Debug and development settings
        bool DebugMode { get; set; }
        bool ShowPerformanceMetrics { get; set; }
        string LogLevel { get; set; }
        
        // Backup and safety settings
        bool AutoBackup { get; set; }
        int BackupRetentionDays { get; set; }
        string BackupPath { get; set; }
        
        // Events for UI synchronization
        event EventHandler<string> ConfigurationChanged;
    }
}
