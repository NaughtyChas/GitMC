using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitMC.Extensions;
using GitMC.Models;
using GitMC.Utils;

namespace GitMC.Services;

/// <summary>
///     Service for managing saved Minecraft worlds
/// </summary>
public class ManagedSaveService
{
    private readonly IDataStorageService _dataStorageService;
    private readonly ILoggingService _logger;

    public ManagedSaveService(IDataStorageService dataStorageService)
    {
        _dataStorageService = dataStorageService;
        _logger = ServiceFactory.Logger;
    }

    /// <summary>
    ///     Gets all managed saves
    /// </summary>
    /// <returns>List of managed save information</returns>
    public async Task<List<ManagedSaveInfo>> GetManagedSaves()
    {
        var saves = new List<ManagedSaveInfo>();
        var managedSavesPath = GetManagedSavesStoragePath();

        if (!Directory.Exists(managedSavesPath))
            return saves;

        var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
        foreach (var jsonFile in jsonFiles)
            try
            {
                var json = await File.ReadAllTextAsync(jsonFile);
                var saveInfo = JsonSerializer.Deserialize<ManagedSaveInfo>(json);
                if (saveInfo != null)
                {
                    // Update size and last modified time with current values
                    await UpdateSaveInfoWithCurrentData(saveInfo);
                    saves.Add(saveInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogCategory.FileSystem, "Failed to parse save info from file: {JsonFile}", ex, jsonFile);
            }

        return saves.OrderByDescending(s => s.LastModified).ToList();
    }

    /// <summary>
    ///     Gets a specific managed save by ID
    /// </summary>
    /// <param name="saveId">Save ID to find</param>
    /// <returns>Managed save information or null if not found</returns>
    public async Task<ManagedSaveInfo?> GetSaveByIdAsync(string saveId)
    {
        try
        {
            var managedSavesPath = GetManagedSavesStoragePath();
            var saveInfoPath = Path.Combine(managedSavesPath, $"{saveId}.json");

            if (!File.Exists(saveInfoPath))
                return null;

            var json = await File.ReadAllTextAsync(saveInfoPath);
            return JsonSerializer.Deserialize<ManagedSaveInfo>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get save by ID {saveId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Updates save info with current data from the filesystem
    /// </summary>
    /// <param name="saveInfo">Save info to update</param>
    private async Task UpdateSaveInfoWithCurrentData(ManagedSaveInfo saveInfo)
    {
        try
        {
            if (Directory.Exists(saveInfo.OriginalPath))
            {
                var directoryInfo = new DirectoryInfo(saveInfo.OriginalPath);

                // Update last modified time
                saveInfo.LastModified = directoryInfo.LastWriteTime;

                // Update size - calculate folder size asynchronously
                saveInfo.Size = await Task.Run(() => CommonHelpers.CalculateFolderSize(directoryInfo));

                // Update Git status if initialized
                if (saveInfo.IsGitInitialized) await UpdateGitStatus(saveInfo);
            }
            else
            {
                // Directory doesn't exist anymore - mark as missing
                saveInfo.Size = 0;
                Debug.WriteLine($"Save directory not found: {saveInfo.OriginalPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update save info for {saveInfo.Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Updates Git status information for a managed save
    /// </summary>
    /// <param name="saveInfo">Save info to update with Git status</param>
    private async Task UpdateGitStatus(ManagedSaveInfo saveInfo)
    {
        try
        {
            // Get Git service from service factory
            var gitService = ServiceFactory.Services.Git;

            // Check if it's a Git repository
            var isRepo = await gitService.IsRepositoryAsync(saveInfo.OriginalPath);
            if (!isRepo)
            {
                // Not a Git repository anymore, mark as not initialized
                saveInfo.IsGitInitialized = false;
                saveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Clear;
                saveInfo.Branch = "main";
                saveInfo.CommitCount = 0;
                saveInfo.PendingPushCount = 0;
                saveInfo.PendingPullCount = 0;
                saveInfo.ConflictCount = 0;
                return;
            }

            // Get Git status for save directory
            var status = await gitService.GetStatusAsync(saveInfo.OriginalPath);

            // Update branch name
            if (!string.IsNullOrEmpty(status.CurrentBranch)) saveInfo.Branch = status.CurrentBranch;

            // Update push/pull counts
            saveInfo.PendingPushCount = status.AheadCount;
            saveInfo.PendingPullCount = status.BehindCount;
            saveInfo.ConflictCount = 0; // For now, we don't handle merge conflicts

            // Update commit count
            try
            {
                var commits = await gitService.GetCommitHistoryAsync(1000, saveInfo.OriginalPath);
                saveInfo.CommitCount = commits.Length;
            }
            catch
            {
                // If we can't get commit history, keep existing count or set to 0
                if (saveInfo.CommitCount == 0) saveInfo.CommitCount = 1; // Assume at least the initial commit exists
            }

            // Determine save status based on Git status
            if (status.HasChanges)
                saveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Modified;
            else if (saveInfo.ConflictCount > 0)
                saveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Conflict;
            else
                saveInfo.CurrentStatus = ManagedSaveInfo.SaveStatus.Clear;

            _logger.LogDebug(LogCategory.Git, 
                "Updated Git status for save '{SaveName}': Status={Status}, Branch={Branch}, Commits={CommitCount}, Push={PushCount}, Pull={PullCount}",
                saveInfo.Name, saveInfo.CurrentStatus, saveInfo.Branch, saveInfo.CommitCount, saveInfo.PendingPushCount, saveInfo.PendingPullCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(LogCategory.Git, "Failed to update Git status for save '{SaveName}'", ex, saveInfo.Name);
            // Don't throw - just leave Git status as is
        }
    }

    /// <summary>
    ///     Gets the count of managed saves
    /// </summary>
    /// <returns>Number of managed saves</returns>
    public int GetManagedSavesCount()
    {
        try
        {
            var managedSavesPath = GetManagedSavesStoragePath();
            if (!Directory.Exists(managedSavesPath))
                return 0;

            var jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
            return jsonFiles.Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    ///     Gets the managed saves storage path
    /// </summary>
    /// <returns>Path to managed saves storage directory</returns>
    public string GetManagedSavesStoragePath()
    {
        return _dataStorageService.GetManagedSavesDirectory();
    }

    /// <summary>
    ///     Registers a new managed save
    /// </summary>
    /// <param name="save">Minecraft save to register</param>
    /// <param name="saveId">Optional custom save ID</param>
    /// <returns>The ID of the registered save</returns>
    public async Task<string> RegisterManagedSave(MinecraftSave save, string? saveId = null)
    {
        try
        {
            Debug.WriteLine($"[RegisterManagedSave] Starting registration for save: {save.Name}");

            var managedSavesPath = GetManagedSavesStoragePath();
            Debug.WriteLine($"[RegisterManagedSave] Managed saves path: {managedSavesPath}");

            if (!Directory.Exists(managedSavesPath))
            {
                Debug.WriteLine($"[RegisterManagedSave] Directory doesn't exist, creating: {managedSavesPath}");
                Directory.CreateDirectory(managedSavesPath);
                Debug.WriteLine("[RegisterManagedSave] Directory created successfully");
            }

            var actualSaveId = saveId ?? GenerateSaveId(save.Name);
            Debug.WriteLine($"[RegisterManagedSave] Generated save ID: {actualSaveId}");

            var saveInfoPath = Path.Combine(managedSavesPath, $"{actualSaveId}.json");
            Debug.WriteLine($"[RegisterManagedSave] Save info path: {saveInfoPath}");

            var saveInfo = new ManagedSaveInfo
            {
                Id = actualSaveId,
                Name = save.Name,
                Path = save.Path,
                OriginalPath = save.Path,
                AddedDate = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                GitRepository = "",
                IsGitInitialized = save.IsGitInitialized,
                Size = save.WorldSize,
                GameVersion = save.GameVersion,
                WorldIcon = save.WorldIcon
            };
            Debug.WriteLine("[RegisterManagedSave] Created save info object");

            // Create JsonSerializerOptions that ignore UI properties
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(saveInfo, jsonOptions);
            Debug.WriteLine($"[RegisterManagedSave] Serialized to JSON, length: {json.Length}");

            Debug.WriteLine($"[RegisterManagedSave] About to write file to: {saveInfoPath}");
            await File.WriteAllTextAsync(saveInfoPath, json);
            Debug.WriteLine("[RegisterManagedSave] File written successfully!");

            return actualSaveId;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RegisterManagedSave] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"[RegisterManagedSave] Stack trace: {ex.StackTrace}");
            throw; // Re-throw the exception so the caller can handle it appropriately
        }
    }

    /// <summary>
    ///     Updates an existing managed save
    /// </summary>
    /// <param name="saveInfo">Save info to update</param>
    /// <returns>Task</returns>
    public async Task UpdateManagedSave(ManagedSaveInfo saveInfo)
    {
        try
        {
            var managedSavesPath = GetManagedSavesStoragePath();
            var saveInfoPath = Path.Combine(managedSavesPath, $"{saveInfo.Id}.json");

            if (File.Exists(saveInfoPath))
            {
                // Create JsonSerializerOptions that ignore UI properties
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(saveInfo, jsonOptions);
                await File.WriteAllTextAsync(saveInfoPath, json);
                Debug.WriteLine($"[UpdateManagedSave] Updated save info for: {saveInfo.Name}");
            }
            else
            {
                Debug.WriteLine($"[UpdateManagedSave] Save info file not found: {saveInfoPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateManagedSave] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Refreshes Git status for a specific managed save
    /// </summary>
    /// <param name="saveInfo">Save info to refresh Git status for</param>
    /// <returns>Task</returns>
    public async Task RefreshGitStatus(ManagedSaveInfo saveInfo)
    {
        if (saveInfo.IsGitInitialized)
        {
            await UpdateGitStatus(saveInfo);
            await UpdateManagedSave(saveInfo);
        }
    }

    /// <summary>
    ///     Refreshes Git status for all managed saves
    /// </summary>
    /// <returns>Task</returns>
    public async Task RefreshAllGitStatus()
    {
        var saves = await GetManagedSaves();
        var gitUpdates = saves.Where(s => s.IsGitInitialized).Select(RefreshGitStatus);
        await Task.WhenAll(gitUpdates);
    }

    /// <summary>
    ///     Generates a unique save ID for managed saves
    /// </summary>
    /// <param name="saveName">Name of the save</param>
    /// <returns>Unique save ID</returns>
    private string GenerateSaveId(string saveName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", saveName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{safeName}_{timestamp}";
    }
}