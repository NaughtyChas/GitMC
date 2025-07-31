using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using GitMC.Models;
using GitMC.Services;
using GitMC.Utils;

namespace GitMC.ViewModels;

public class SaveManagementViewModel : BaseViewModel
{
    private readonly IConfigurationService _configurationService;
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private readonly IOnboardingService _onboardingService;
    private readonly SaveAnalyzerService _saveAnalyzerService;

    private bool _isLoading;
    private int _totalSavesCount;
    private int _savesWithChangesCount;
    private int _remoteUpdatesCount;
    private string _gitStatusText = "Ready";
    private string _totalSizeText = "0 B";

    public SaveManagementViewModel(
        IConfigurationService configurationService,
        IDataStorageService dataStorageService,
        IGitService gitService,
        IOnboardingService onboardingService,
        SaveAnalyzerService saveAnalyzerService)
    {
        _configurationService = configurationService;
        _dataStorageService = dataStorageService;
        _gitService = gitService;
        _onboardingService = onboardingService;
        _saveAnalyzerService = saveAnalyzerService;

        ManagedSaves = new ObservableCollection<ManagedSaveInfo>();
    }

    public ObservableCollection<ManagedSaveInfo> ManagedSaves { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public int TotalSavesCount
    {
        get => _totalSavesCount;
        set => SetProperty(ref _totalSavesCount, value);
    }

    public int SavesWithChangesCount
    {
        get => _savesWithChangesCount;
        set => SetProperty(ref _savesWithChangesCount, value);
    }

    public int RemoteUpdatesCount
    {
        get => _remoteUpdatesCount;
        set => SetProperty(ref _remoteUpdatesCount, value);
    }

    public string GitStatusText
    {
        get => _gitStatusText;
        set => SetProperty(ref _gitStatusText, value);
    }

    public string TotalSizeText
    {
        get => _totalSizeText;
        set => SetProperty(ref _totalSizeText, value);
    }

    public bool HasSaves => ManagedSaves.Count > 0;
    public bool HasNoSaves => !HasSaves;

    public async Task LoadManagedSavesAsync()
    {
        try
        {
            IsLoading = true;

            var saves = await GetManagedSavesAsync();

            ManagedSaves.Clear();
            foreach (var save in saves)
            {
                ManagedSaves.Add(save);
            }

            UpdateStatistics();

            // Notify UI about collection changes
            OnPropertyChanged(nameof(HasSaves));
            OnPropertyChanged(nameof(HasNoSaves));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading managed saves: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<ManagedSaveInfo>> GetManagedSavesAsync()
    {
        var saves = new List<ManagedSaveInfo>();

        try
        {
            string managedSavesPath = GetManagedSavesStoragePath();

            if (!Directory.Exists(managedSavesPath))
                return saves;

            string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");

            foreach (string jsonFile in jsonFiles)
            {
                try
                {
                    string jsonContent = await File.ReadAllTextAsync(jsonFile);
                    var saveInfo = System.Text.Json.JsonSerializer.Deserialize<ManagedSaveInfo>(jsonContent);

                    if (saveInfo != null)
                    {
                        // Update computed properties if needed
                        if (Directory.Exists(saveInfo.Path))
                        {
                            var dirInfo = new DirectoryInfo(saveInfo.Path);
                            saveInfo.LastModified = dirInfo.LastWriteTime;
                            saveInfo.Size = await CalculateFolderSizeAsync(saveInfo.Path);
                        }

                        saves.Add(saveInfo);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading save info from {jsonFile}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting managed saves: {ex.Message}");
        }

        return saves.OrderByDescending(s => s.LastModified).ToList();
    }

    private void UpdateStatistics()
    {
        TotalSavesCount = ManagedSaves.Count;
        SavesWithChangesCount = ManagedSaves.Count(s => s.HasPendingChanges);
        RemoteUpdatesCount = 0; // TODO: Implement remote updates check

        // Calculate total size
        long totalSize = ManagedSaves.Sum(s => s.Size);
        TotalSizeText = CommonHelpers.FormatFileSize(totalSize);
    }

    private string GetManagedSavesStoragePath()
    {
        try
        {
            return _dataStorageService.GetManagedSavesDirectory();
        }
        catch
        {
            // Fallback to default path
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, "GitMC", "ManagedSaves");
        }
    }

    private async Task<long> CalculateFolderSizeAsync(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return 0;

            return await Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(folderPath);
                return dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            });
        }
        catch
        {
            return 0;
        }
    }
}

