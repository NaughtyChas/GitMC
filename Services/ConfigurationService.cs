using System.ComponentModel;
using System.Text.Json;

namespace GitMC.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configFilePath;
        private Dictionary<string, object> _settings = new();
        private readonly object _lock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? ConfigurationChanged;

        public ConfigurationService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var gitMcFolder = Path.Combine(appDataPath, "GitMC");
            
            // Ensure directory exists
            if (!Directory.Exists(gitMcFolder))
            {
                Directory.CreateDirectory(gitMcFolder);
            }
            
            _configFilePath = Path.Combine(gitMcFolder, "config.json");
        }

        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    
                    lock (_lock)
                    {
                        _settings.Clear();
                        if (loaded != null)
                        {
                            foreach (var kvp in loaded)
                            {
                                _settings[kvp.Key] = ConvertJsonElement(kvp.Value);
                            }
                        }
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
            try
            {
                string json;
                lock (_lock)
                {
                    json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                }
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch
            {
                // Ignore save errors - we don't want to crash the app
            }
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value) && value is bool boolValue)
                {
                    return boolValue;
                }
                return defaultValue;
            }
        }

        public void SetBool(string key, bool value)
        {
            bool changed = false;
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
                // Auto-save on every change
                _ = Task.Run(SaveAsync);
                ConfigurationChanged?.Invoke(this, key);
                NotifySpecificPropertyChanged(key);
            }
        }

        public string GetString(string key, string defaultValue = "")
        {
            lock (_lock)
            {
                if (_settings.TryGetValue(key, out var value) && value is string stringValue)
                {
                    return stringValue;
                }
                return defaultValue;
            }
        }

        public void SetString(string key, string value)
        {
            bool changed = false;
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
            bool changed = false;
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
            get => GetInt("CurrentOnboardingStep", 0);
            set => SetInt("CurrentOnboardingStep", value);
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
                _ => element.ToString()
            };
        }

        private void NotifyAllPropertiesChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLanguageConfigured)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlatformConfigured)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSaveAdded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFirstLaunchComplete)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOnboardingStep)));
        }

        private void NotifySpecificPropertyChanged(string key)
        {
            var propertyName = key switch
            {
                "LanguageConfigured" => nameof(IsLanguageConfigured),
                "PlatformConfigured" => nameof(IsPlatformConfigured),
                "SaveAdded" => nameof(IsSaveAdded),
                "FirstLaunchComplete" => nameof(IsFirstLaunchComplete),
                "CurrentOnboardingStep" => nameof(CurrentOnboardingStep),
                _ => null
            };

            if (propertyName != null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
