using System.ComponentModel;
using System.Text.Json;

namespace GitMC.Services;

public class ConfigurationService : IConfigurationService
{
    private const int SaveDelayMs = 500; // Debounce save operations
    private readonly string _configFilePath;
    private readonly IDataStorageService _dataStorageService;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private readonly Dictionary<string, object> _settings = new();
    private CancellationTokenSource _saveDelayTokenSource = new();

    public ConfigurationService()
    {
        _dataStorageService = new DataStorageService();
        _configFilePath = _dataStorageService.GetConfigurationFilePath();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? ConfigurationChanged;

    public async Task LoadAsync()
    {
        try
        {
            // Ensure data directories exist first
            await _dataStorageService.EnsureDirectoriesExistAsync().ConfigureAwait(false);

            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                var loaded =
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                lock (_lock)
                {
                    _settings.Clear();
                    if (loaded != null)
                        foreach (var kvp in loaded)
                            _settings[kvp.Key] = ConvertJsonElement(kvp.Value);
                }

                // Notify all properties changed after loading
                NotifyAllPropertiesChanged();
            }
        }
        catch
        {
            // If loading fails, start with empty settings
            lock (_lock)
            {
                _settings.Clear();
            }
        }
    }

    public async Task SaveAsync()
    {
        await _saveSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            }

            await File.WriteAllTextAsync(_configFilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Ignore save errors - we don't want to crash the app
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value) && value is bool boolValue) return boolValue;
            return defaultValue;
        }
    }

    public void SetBool(string key, bool value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue) || !currentValue.Equals(value))
            {
                _settings[key] = value;
                changed = true;
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    public string GetString(string key, string defaultValue = "")
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value) && value is string stringValue) return stringValue;
            return defaultValue;
        }
    }

    public void SetString(string key, string value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue) || !currentValue.Equals(value))
            {
                _settings[key] = value;
                changed = true;
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                if (value is int intValue)
                    return intValue;
                if (value is double doubleValue && doubleValue == Math.Floor(doubleValue))
                    return (int)doubleValue;
                if (value is JsonElement element && element.TryGetInt32(out var jsonInt))
                    return jsonInt;
            }

            return defaultValue;
        }
    }

    public void SetInt(string key, int value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue) || !currentValue.Equals(value))
            {
                _settings[key] = value;
                changed = true;
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    // Convenient properties for onboarding
    public bool IsLanguageConfigured
    {
        get => GetBool("LanguageConfigured");
        set => SetBool("LanguageConfigured", value);
    }

    public bool IsPlatformConfigured
    {
        get => GetBool("PlatformConfigured");
        set => SetBool("PlatformConfigured", value);
    }

    public bool IsSaveAdded
    {
        get => GetBool("SaveAdded");
        set => SetBool("SaveAdded", value);
    }

    public bool IsFirstLaunchComplete
    {
        get => GetBool("FirstLaunchComplete");
        set => SetBool("FirstLaunchComplete", value);
    }

    public int CurrentOnboardingStep
    {
        get => GetInt("CurrentOnboardingStep");
        set => SetInt("CurrentOnboardingStep", value);
    }

    // New onboarding properties for Git system setup
    public bool IsGitSystemConfigured
    {
        get => GetBool("GitSystemConfigured");
        set => SetBool("GitSystemConfigured", value);
    }

    public bool IsGitIdentityConfigured
    {
        get => GetBool("GitIdentityConfigured");
        set => SetBool("GitIdentityConfigured", value);
    }

    // Application preferences
    public string CurrentLanguage
    {
        get => GetString("CurrentLanguage", "en-US");
        set => SetString("CurrentLanguage", value);
    }

    public string Theme
    {
        get => GetString("Theme", "Default");
        set => SetString("Theme", value);
    }

    // Window state and layout
    public double WindowWidth
    {
        get => GetDouble("WindowWidth", 1200);
        set => SetDouble("WindowWidth", value);
    }

    public double WindowHeight
    {
        get => GetDouble("WindowHeight", 800);
        set => SetDouble("WindowHeight", value);
    }

    public double WindowX
    {
        get => GetDouble("WindowX", 100);
        set => SetDouble("WindowX", value);
    }

    public double WindowY
    {
        get => GetDouble("WindowY", 100);
        set => SetDouble("WindowY", value);
    }

    public bool IsMaximized
    {
        get => GetBool("IsMaximized");
        set => SetBool("IsMaximized", value);
    }

    // Git configuration
    public string LastUsedGitPath
    {
        get => GetString("LastUsedGitPath");
        set => SetString("LastUsedGitPath", value);
    }

    public string DefaultGitUserName
    {
        get => GetString("DefaultGitUserName");
        set => SetString("DefaultGitUserName", value);
    }

    public string DefaultGitUserEmail
    {
        get => GetString("DefaultGitUserEmail");
        set => SetString("DefaultGitUserEmail", value);
    }

    // Repository and save management
    public string LastOpenedSavePath
    {
        get => GetString("LastOpenedSavePath");
        set => SetString("LastOpenedSavePath", value);
    }

    public string[] RecentSaves
    {
        get => GetStringArray("RecentSaves");
        set => SetStringArray("RecentSaves", value);
    }

    public int MaxRecentSaves
    {
        get => GetInt("MaxRecentSaves", 10);
        set => SetInt("MaxRecentSaves", value);
    }

    // Platform settings
    public string SelectedPlatform
    {
        get => GetString("SelectedPlatform", "Local");
        set => SetString("SelectedPlatform", value);
    }

    public string PlatformToken
    {
        get => GetString("PlatformToken");
        set => SetString("PlatformToken", value);
    }

    public string PlatformUsername
    {
        get => GetString("PlatformUsername");
        set => SetString("PlatformUsername", value);
    }

    public string PlatformEmail
    {
        get => GetString("PlatformEmail");
        set => SetString("PlatformEmail", value);
    }

    // GitHub specific settings
    public string GitHubAccessToken
    {
        get => GetString("GitHubAccessToken");
        set => SetString("GitHubAccessToken", value);
    }

    public DateTime GitHubAccessTokenTimestamp
    {
        get => GetDateTime("GitHubAccessTokenTimestamp", DateTime.MinValue);
        set => SetDateTime("GitHubAccessTokenTimestamp", value);
    }

    public string GitHubUsername
    {
        get => GetString("GitHubUsername");
        set => SetString("GitHubUsername", value);
    }

    public string GitHubRepository
    {
        get => GetString("GitHubRepository");
        set => SetString("GitHubRepository", value);
    }

    public bool GitHubPrivateRepo
    {
        get => GetBool("GitHubPrivateRepo", true);
        set => SetBool("GitHubPrivateRepo", value);
    }

    // Git server settings (for self-hosted)
    public string GitServerUrl
    {
        get => GetString("GitServerUrl");
        set => SetString("GitServerUrl", value);
    }

    public string GitUsername
    {
        get => GetString("GitUsername");
        set => SetString("GitUsername", value);
    }

    public string GitAccessToken
    {
        get => GetString("GitAccessToken");
        set => SetString("GitAccessToken", value);
    }

    public string GitServerType
    {
        get => GetString("GitServerType");
        set => SetString("GitServerType", value);
    }

    // Debug and development settings
    public bool DebugMode
    {
        get => GetBool("DebugMode");
        set => SetBool("DebugMode", value);
    }

    public bool ShowPerformanceMetrics
    {
        get => GetBool("ShowPerformanceMetrics");
        set => SetBool("ShowPerformanceMetrics", value);
    }

    public string LogLevel
    {
        get => GetString("LogLevel", "Info");
        set => SetString("LogLevel", value);
    }

    // Backup and safety settings
    public bool AutoBackup
    {
        get => GetBool("AutoBackup", true);
        set => SetBool("AutoBackup", value);
    }

    public int BackupRetentionDays
    {
        get => GetInt("BackupRetentionDays", 30);
        set => SetInt("BackupRetentionDays", value);
    }

    public string BackupPath
    {
        get => GetString("BackupPath");
        set => SetString("BackupPath", value);
    }

    public DateTime GetDateTime(string key, DateTime defaultValue = default)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                if (value is DateTime dateTimeValue)
                    return dateTimeValue;
                if (value is string stringValue && DateTime.TryParse(stringValue, out var parsedDate))
                    return parsedDate;
                if (value is JsonElement element && element.TryGetDateTime(out var jsonDate))
                    return jsonDate;
            }

            return defaultValue;
        }
    }

    public void SetDateTime(string key, DateTime value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue) || !currentValue.Equals(value))
            {
                _settings[key] = value;
                changed = true;
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    // Array handling methods
    public string[] GetStringArray(string key, string[]? defaultValue = null)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                if (value is string[] stringArray)
                    return stringArray;
                if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var item in element.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String)
                            items.Add(item.GetString() ?? "");
                    return items.ToArray();
                }
            }

            return defaultValue ?? Array.Empty<string>();
        }
    }

    public void SetStringArray(string key, string[] value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue))
            {
                _settings[key] = value;
                changed = true;
            }
            else
            {
                var currentStringArray = currentValue as string[];
                if (!ArraysEqual(currentStringArray, value))
                {
                    _settings[key] = value;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    public double GetDouble(string key, double defaultValue = 0.0)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                if (value is double doubleValue)
                    return doubleValue;
                if (value is int intValue)
                    return intValue;
                if (value is JsonElement element && element.TryGetDouble(out var jsonDouble))
                    return jsonDouble;
            }

            return defaultValue;
        }
    }

    public void SetDouble(string key, double value)
    {
        var changed = false;
        lock (_lock)
        {
            if (!_settings.TryGetValue(key, out var currentValue) || !currentValue.Equals(value))
            {
                _settings[key] = value;
                changed = true;
            }
        }

        if (changed)
        {
            _ = Task.Run(SaveAsync);
            ConfigurationChanged?.Invoke(this, key);
            NotifySpecificPropertyChanged(key);
        }
    }

    private bool ArraysEqual(string[]? array1, string[]? array2)
    {
        if (array1 == null && array2 == null) return true;
        if (array1 == null || array2 == null) return false;
        if (array1.Length != array2.Length) return false;

        for (var i = 0; i < array1.Length; i++)
            if (array1[i] != array2[i])
                return false;
        return true;
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => ConvertJsonArray(element),
            _ => element.ToString()
        };
    }

    private string[] ConvertJsonArray(JsonElement arrayElement)
    {
        var items = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                items.Add(item.GetString() ?? "");
            else
                items.Add(item.ToString());
        return items.ToArray();
    }

    private void NotifyAllPropertiesChanged()
    {
        // Onboarding properties
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLanguageConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlatformConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSaveAdded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFirstLaunchComplete)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOnboardingStep)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGitSystemConfigured)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGitIdentityConfigured)));

        // Application preferences
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));

        // Window state
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowHeight)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowX)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowY)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMaximized)));

        // Git settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUsedGitPath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultGitUserName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultGitUserEmail)));

        // Save management
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastOpenedSavePath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecentSaves)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxRecentSaves)));

        // Platform settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPlatform)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlatformToken)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlatformUsername)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlatformEmail)));

        // GitHub settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubAccessToken)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubAccessTokenTimestamp)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubUsername)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubRepository)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubPrivateRepo)));

        // Git server settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitServerUrl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitUsername)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitAccessToken)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitServerType)));

        // Debug settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebugMode)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowPerformanceMetrics)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LogLevel)));

        // Backup settings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoBackup)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackupRetentionDays)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackupPath)));
    }

    private void NotifySpecificPropertyChanged(string key)
    {
        var propertyName = key switch
        {
            // Onboarding properties
            "LanguageConfigured" => nameof(IsLanguageConfigured),
            "PlatformConfigured" => nameof(IsPlatformConfigured),
            "SaveAdded" => nameof(IsSaveAdded),
            "FirstLaunchComplete" => nameof(IsFirstLaunchComplete),
            "CurrentOnboardingStep" => nameof(CurrentOnboardingStep),
            "GitSystemConfigured" => nameof(IsGitSystemConfigured),
            "GitIdentityConfigured" => nameof(IsGitIdentityConfigured),

            // Application preferences
            "CurrentLanguage" => nameof(CurrentLanguage),
            "Theme" => nameof(Theme),

            // Window state
            "WindowWidth" => nameof(WindowWidth),
            "WindowHeight" => nameof(WindowHeight),
            "WindowX" => nameof(WindowX),
            "WindowY" => nameof(WindowY),
            "IsMaximized" => nameof(IsMaximized),

            // Git settings
            "LastUsedGitPath" => nameof(LastUsedGitPath),
            "DefaultGitUserName" => nameof(DefaultGitUserName),
            "DefaultGitUserEmail" => nameof(DefaultGitUserEmail),

            // Save management
            "LastOpenedSavePath" => nameof(LastOpenedSavePath),
            "RecentSaves" => nameof(RecentSaves),
            "MaxRecentSaves" => nameof(MaxRecentSaves),

            // Platform settings
            "SelectedPlatform" => nameof(SelectedPlatform),
            "PlatformToken" => nameof(PlatformToken),
            "PlatformUsername" => nameof(PlatformUsername),
            "PlatformEmail" => nameof(PlatformEmail),

            // GitHub settings
            "GitHubAccessToken" => nameof(GitHubAccessToken),
            "GitHubAccessTokenTimestamp" => nameof(GitHubAccessTokenTimestamp),
            "GitHubUsername" => nameof(GitHubUsername),
            "GitHubRepository" => nameof(GitHubRepository),
            "GitHubPrivateRepo" => nameof(GitHubPrivateRepo),

            // Git server settings
            "GitServerUrl" => nameof(GitServerUrl),
            "GitUsername" => nameof(GitUsername),
            "GitAccessToken" => nameof(GitAccessToken),
            "GitServerType" => nameof(GitServerType),

            // Debug settings
            "DebugMode" => nameof(DebugMode),
            "ShowPerformanceMetrics" => nameof(ShowPerformanceMetrics),
            "LogLevel" => nameof(LogLevel),

            // Backup settings
            "AutoBackup" => nameof(AutoBackup),
            "BackupRetentionDays" => nameof(BackupRetentionDays),
            "BackupPath" => nameof(BackupPath),

            _ => null
        };

        if (propertyName != null) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}