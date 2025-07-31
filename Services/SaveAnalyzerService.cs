using System.Diagnostics;
using GitMC.Models;
using GitMC.Utils;

namespace GitMC.Services;

/// <summary>
///     Service for analyzing Minecraft save folders
/// </summary>
public class SaveAnalyzerService
{
    /// <summary>
    ///     Analyzes a save folder and creates a MinecraftSave object
    /// </summary>
    /// <param name="savePath">Path to the save folder</param>
    /// <returns>MinecraftSave object if valid, null otherwise</returns>
    public async Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                string levelDatPath = Path.Combine(savePath, "level.dat");
                string levelDatOldPath = Path.Combine(savePath, "level.dat_old");

                if (!File.Exists(levelDatPath) && !File.Exists(levelDatOldPath))
                    return null;

                var directoryInfo = new DirectoryInfo(savePath);
                var save = new MinecraftSave
                {
                    Name = directoryInfo.Name,
                    Path = savePath,
                    LastPlayed = directoryInfo.LastWriteTime,
                    WorldSize = CommonHelpers.CalculateFolderSize(directoryInfo),
                    IsGitInitialized = Directory.Exists(Path.Combine(savePath, "GitMC")),
                    WorldType = "Survival", // Default value
                    GameVersion = "1.21" // Default value
                };

                save.WorldIcon = CommonHelpers.GetWorldIcon(save.WorldType);

                return save;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to analyze save folder: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    ///     Generates a unique save ID for managed saves
    /// </summary>
    /// <param name="saveName">Name of the save</param>
    /// <returns>Unique save ID</returns>
    public string GenerateSaveId(string saveName)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string safeName = string.Join("_", saveName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"{safeName}_{timestamp}";
    }
}
