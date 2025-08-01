using System.Collections.ObjectModel;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
/// Service for managing save initialization process
/// </summary>
public interface ISaveInitializationService
{
    /// <summary>
    /// Initialize a save with Git version control
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="progress">Progress callback for step updates</param>
    /// <returns>True if initialization was successful</returns>
    Task<bool> InitializeSaveAsync(string savePath, IProgress<SaveInitStep>? progress = null);

    /// <summary>
    /// Get the list of initialization steps
    /// </summary>
    /// <returns>Collection of initialization steps</returns>
    ObservableCollection<SaveInitStep> GetInitializationSteps();
}
