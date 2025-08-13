namespace GitMC.Services;

/// <summary>
///     Service for managing all data storage in the .GitMC folder
/// </summary>
public interface IDataStorageService
{
    /// <summary>
    ///     Get the root data directory (.GitMC)
    /// </summary>
    string GetDataDirectory();

    /// <summary>
    ///     Get the managed saves storage directory
    /// </summary>
    string GetManagedSavesDirectory();

    /// <summary>
    ///     Get the configuration file path
    /// </summary>
    string GetConfigurationFilePath();

    /// <summary>
    ///     Get the backups directory
    /// </summary>
    string GetBackupsDirectory();

    /// <summary>
    ///     Get the logs directory
    /// </summary>
    string GetLogsDirectory();

    /// <summary>
    ///     Get the cache directory
    /// </summary>
    string GetCacheDirectory();

    /// <summary>
    ///     Ensure all necessary directories exist
    /// </summary>
    Task EnsureDirectoriesExistAsync();

    /// <summary>
    ///     Check if the data directory is accessible
    /// </summary>
    bool IsDataDirectoryAccessible();
}