using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitMC.Models;
using GitMC.Utils;

namespace GitMC.Services;

/// <summary>
///     Service for managing saved Minecraft worlds
/// </summary>
public class ManagedSaveService
{
    private readonly IDataStorageService _dataStorageService;

    public ManagedSaveService(IDataStorageService dataStorageService)
    {
        _dataStorageService = dataStorageService;
    }

    /// <summary>
    ///     Gets all managed saves
    /// </summary>
    /// <returns>List of managed save information</returns>
    public async Task<List<ManagedSaveInfo>> GetManagedSaves()
    {
        var saves = new List<ManagedSaveInfo>();
        string managedSavesPath = GetManagedSavesStoragePath();

        if (!Directory.Exists(managedSavesPath))
            return saves;

        string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
        foreach (string jsonFile in jsonFiles)
            try
            {
                string json = await File.ReadAllTextAsync(jsonFile);
                ManagedSaveInfo? saveInfo = JsonSerializer.Deserialize<ManagedSaveInfo>(json);
                if (saveInfo != null)
                {
                    // Update size and last modified time with current values
                    await UpdateSaveInfoWithCurrentData(saveInfo);
                    saves.Add(saveInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse save info from {jsonFile}: {ex.Message}");
            }

        return saves.OrderByDescending(s => s.LastModified).ToList();
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
    ///     Gets the count of managed saves
    /// </summary>
    /// <returns>Number of managed saves</returns>
    public int GetManagedSavesCount()
    {
        try
        {
            string managedSavesPath = GetManagedSavesStoragePath();
            if (!Directory.Exists(managedSavesPath))
                return 0;

            string[] jsonFiles = Directory.GetFiles(managedSavesPath, "*.json");
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
    /// <returns>Task</returns>
    public async Task RegisterManagedSave(MinecraftSave save, string? saveId = null)
    {
        try
        {
            Debug.WriteLine($"[RegisterManagedSave] Starting registration for save: {save.Name}");

            string managedSavesPath = GetManagedSavesStoragePath();
            Debug.WriteLine($"[RegisterManagedSave] Managed saves path: {managedSavesPath}");

            if (!Directory.Exists(managedSavesPath))
            {
                Debug.WriteLine($"[RegisterManagedSave] Directory doesn't exist, creating: {managedSavesPath}");
                Directory.CreateDirectory(managedSavesPath);
                Debug.WriteLine("[RegisterManagedSave] Directory created successfully");
            }

            string actualSaveId = saveId ?? GenerateSaveId(save.Name);
            Debug.WriteLine($"[RegisterManagedSave] Generated save ID: {actualSaveId}");

            string saveInfoPath = Path.Combine(managedSavesPath, $"{actualSaveId}.json");
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
                WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(saveInfo, jsonOptions);
            Debug.WriteLine($"[RegisterManagedSave] Serialized to JSON, length: {json.Length}");

            Debug.WriteLine($"[RegisterManagedSave] About to write file to: {saveInfoPath}");
            await File.WriteAllTextAsync(saveInfoPath, json);
            Debug.WriteLine("[RegisterManagedSave] File written successfully!");
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
            string managedSavesPath = GetManagedSavesStoragePath();
            string saveInfoPath = Path.Combine(managedSavesPath, $"{saveInfo.Id}.json");

            if (File.Exists(saveInfoPath))
            {
                // Create JsonSerializerOptions that ignore UI properties
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                string json = JsonSerializer.Serialize(saveInfo, jsonOptions);
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
    ///     Generates a unique save ID for managed saves
    /// </summary>
    /// <param name="saveName">Name of the save</param>
    /// <returns>Unique save ID</returns>
    private string GenerateSaveId(string saveName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string safeName = string.Join("_", saveName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{safeName}_{timestamp}";
    }
}
