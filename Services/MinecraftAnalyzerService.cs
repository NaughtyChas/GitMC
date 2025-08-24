using System.Globalization;
using GitMC.Models;
using GitMC.Utils;

namespace GitMC.Services;

/// <summary>
///     Minecraft save analyzer service
/// </summary>
public interface IMinecraftAnalyzerService
{
    Task<MinecraftSave?> AnalyzeSaveFolder(string savePath);
    bool ValidateMinecraftSave(string savePath);
    Task<string> ExtractVersionFromNbtInfo(string nbtInfo);
    Task<string> ExtractWorldTypeFromNbtInfo(string nbtInfo);
    string GenerateSaveId(string saveName);
}

/// <summary>
///     Minecraft analyzer service implementation
/// </summary>
public class MinecraftAnalyzerService : IMinecraftAnalyzerService
{
    private readonly INbtService _nbtService;

    public MinecraftAnalyzerService(INbtService nbtService)
    {
        _nbtService = nbtService;
    }

    public async Task<MinecraftSave?> AnalyzeSaveFolder(string savePath)
    {
        try
        {
            if (!ValidateMinecraftSave(savePath))
                return null;

            var directoryInfo = new DirectoryInfo(savePath);
            var save = new MinecraftSave
            {
                Name = directoryInfo.Name,
                Path = savePath,
                LastPlayed = directoryInfo.LastWriteTime,
                WorldSize = CommonHelpers.CalculateFolderSize(directoryInfo),
                IsGitInitialized = Directory.Exists(Path.Combine(savePath, "GitMC")),
                WorldType = "Survival" // Default
            };

            // Analyze NBT data if available
            var levelDatPath = Path.Combine(savePath, "level.dat");
            if (File.Exists(levelDatPath))
                try
                {
                    var nbtInfo = await _nbtService.GetNbtFileInfoAsync(levelDatPath);
                    save.GameVersion = await ExtractVersionFromNbtInfo(nbtInfo);
                    save.WorldType = await ExtractWorldTypeFromNbtInfo(nbtInfo);
                }
                catch
                {
                    // Continue with defaults if NBT reading fails
                }

            // Check Git status
            if (save.IsGitInitialized) save.GitStatus = "Initialized";

            // Set appropriate world icon
            save.WorldIcon = CommonHelpers.GetWorldIcon(save.WorldType);

            return save;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool ValidateMinecraftSave(string savePath)
    {
        var levelDatPath = Path.Combine(savePath, "level.dat");
        var levelDatOldPath = Path.Combine(savePath, "level.dat_old");
        return File.Exists(levelDatPath) || File.Exists(levelDatOldPath);
    }

    public async Task<string> ExtractVersionFromNbtInfo(string nbtInfo)
    {
        await Task.CompletedTask; // For async consistency

        // Parse version from NBT info - this is a simplified implementation
        if (nbtInfo.Contains("Version"))
            // Extract version info - would need proper NBT parsing
            return "1.21"; // Default for now
        return "Unknown";
    }

    public async Task<string> ExtractWorldTypeFromNbtInfo(string nbtInfo)
    {
        await Task.CompletedTask; // For async consistency

        // Parse world type from NBT info - simplified implementation
        if (nbtInfo.Contains("creative") || nbtInfo.Contains("Creative"))
            return "Creative";
        if (nbtInfo.Contains("hardcore") || nbtInfo.Contains("Hardcore"))
            return "Hardcore";
        return "Survival";
    }

    public string GenerateSaveId(string saveName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", saveName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{safeName}_{timestamp}";
    }
}