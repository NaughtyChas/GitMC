using System.Collections.ObjectModel;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
///     Service for managing save initialization process
/// </summary>
public interface ISaveInitializationService
{
    /// <summary>
    ///     Initialize a save with Git version control
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="progress">Progress callback for step updates</param>
    /// <returns>True if initialization was successful</returns>
    Task<bool> InitializeSaveAsync(string savePath, IProgress<SaveInitStep>? progress = null);

    /// <summary>
    ///     Get the list of initialization steps
    /// </summary>
    /// <returns>Collection of initialization steps</returns>
    ObservableCollection<SaveInitStep> GetInitializationSteps();

    /// <summary>
    ///     Commit ongoing changes to the save using partial storage
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="commitMessage">Message describing the changes</param>
    /// <param name="progress">Progress callback for step updates</param>
    /// <returns>True if commit was successful</returns>
    Task<bool> CommitOngoingChangesAsync(string savePath, string commitMessage, IProgress<SaveInitStep>? progress = null);

    /// <summary>
    ///     Translate ongoing changes to SNBT without committing
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="progress">Progress callback for step updates</param>
    /// <returns>True if translation completed (even if no changes)</returns>
    Task<bool> TranslateChangedAsync(string savePath, IProgress<SaveInitStep>? progress = null);

    /// <summary>
    ///     Determine whether there are pending translations (missing or stale SNBT) for the save
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <returns>True if translation should run to bring SNBT current</returns>
    Task<bool> HasPendingTranslationsAsync(string savePath);

    /// <summary>
    ///     Detect changed chunks compared to the last committed state
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <returns>List of changed chunk file paths</returns>
    Task<List<string>> DetectChangedChunksAsync(string savePath);
}
