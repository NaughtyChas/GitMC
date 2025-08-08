using System.Diagnostics;
using System.Reflection;

namespace GitMC.Services;

/// <summary>
///     Service for managing all data storage in the .GitMC folder
///     Organizes all application data in a hidden folder next to the executable
/// </summary>
public class DataStorageService : IDataStorageService
{
    private readonly string _dataDirectory;

    public DataStorageService()
    {
        // Get the executable directory
        var exeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ??
                           Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ??
                           Environment.CurrentDirectory;

        if (string.IsNullOrEmpty(exeDirectory))
        {
            Debug.WriteLine(
                "[DataStorageService] Warning: Could not determine executable directory, using current directory");
            exeDirectory = Environment.CurrentDirectory;
        }

        // Create the .GitMC hidden directory
        _dataDirectory = Path.Combine(exeDirectory, ".GitMC");
        Debug.WriteLine($"[DataStorageService] Data directory set to: {_dataDirectory}");
    }

    public string GetDataDirectory()
    {
        return _dataDirectory;
    }

    public string GetManagedSavesDirectory()
    {
        return Path.Combine(_dataDirectory, "saves");
    }

    public string GetConfigurationFilePath()
    {
        return Path.Combine(_dataDirectory, "config.json");
    }

    public string GetBackupsDirectory()
    {
        return Path.Combine(_dataDirectory, "backups");
    }

    public string GetLogsDirectory()
    {
        return Path.Combine(_dataDirectory, "logs");
    }

    public string GetCacheDirectory()
    {
        return Path.Combine(_dataDirectory, "cache");
    }

    public async Task EnsureDirectoriesExistAsync()
    {
        await Task.Run(async () =>
        {
            try
            {
                // Create main data directory and set as hidden
                if (!Directory.Exists(_dataDirectory))
                {
                    var dirInfo = Directory.CreateDirectory(_dataDirectory);
                    // Set hidden attribute on Windows
                    if (OperatingSystem.IsWindows()) dirInfo.Attributes |= FileAttributes.Hidden;
                }

                // Create subdirectories
                Directory.CreateDirectory(GetManagedSavesDirectory());
                Directory.CreateDirectory(GetBackupsDirectory());
                Directory.CreateDirectory(GetLogsDirectory());
                Directory.CreateDirectory(GetCacheDirectory());

                // Create a README file to explain the folder structure
                var readmePath = Path.Combine(_dataDirectory, "README.txt");
                if (!File.Exists(readmePath))
                {
                    var readmeContent = @"GitMC Data Directory
===================

This folder contains all GitMC application data:

/saves/     - Managed Minecraft save metadata
/backups/   - Save backups and snapshots
/logs/      - Application logs
/cache/     - Temporary files and cache
config.json - Application configuration

This folder can be safely moved with the GitMC executable for portable deployment.
Do not delete this folder unless you want to reset all GitMC data.
";
                    await File.WriteAllTextAsync(readmePath, readmeContent).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Debug.WriteLine($"Failed to create data directories: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    public bool IsDataDirectoryAccessible()
    {
        try
        {
            // Test if we can create and delete a test file
            var testFile = Path.Combine(_dataDirectory, ".test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}