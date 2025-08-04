using System.Collections.ObjectModel;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
///     Service to maintain initialization state across page navigation
/// </summary>
public class SaveInitializationStatusService
{
    private static SaveInitializationStatusService? instance;

    /// <summary>
    ///     List of progress update handlers
    /// </summary>
    private readonly List<Action<SaveInitStep>> _progressHandlers = new();

    private SaveInitializationStatusService() { }
    public static SaveInitializationStatusService Instance => instance ??= new SaveInitializationStatusService();

    /// <summary>
    ///     Whether a save initialization is currently in progress
    /// </summary>
    public bool IsInitializing { get; set; }

    /// <summary>
    ///     The save currently being initialized
    /// </summary>
    public ManagedSaveInfo? CurrentInitializingSave { get; set; }

    /// <summary>
    ///     The current initialization steps
    /// </summary>
    public ObservableCollection<SaveInitStep>? InitSteps { get; set; }

    /// <summary>
    ///     Current progress handler for the active initialization
    /// </summary>
    public IProgress<SaveInitStep>? CurrentProgressHandler { get; set; }

    /// <summary>
    ///     Start tracking initialization for a save
    /// </summary>
    public void StartInitialization(ManagedSaveInfo saveInfo, ObservableCollection<SaveInitStep> steps)
    {
        IsInitializing = true;
        CurrentInitializingSave = saveInfo;
        InitSteps = steps;
    }

    /// <summary>
    ///     Set the progress handler for the current initialization
    /// </summary>
    public void SetProgressHandler(IProgress<SaveInitStep> progressHandler)
    {
        CurrentProgressHandler = progressHandler;
    }

    /// <summary>
    ///     Report progress to the current handler and notify all subscribers
    /// </summary>
    public void ReportProgress(SaveInitStep step)
    {
        // Update the step in our stored collection
        if (InitSteps != null)
        {
            SaveInitStep? existingStep = InitSteps.FirstOrDefault(s => s.Name == step.Name);
            if (existingStep != null)
            {
                existingStep.Status = step.Status;
                existingStep.Message = step.Message;
                existingStep.CurrentProgress = step.CurrentProgress;
                existingStep.TotalProgress = step.TotalProgress;
            }
        }

        // Report to the original progress handler (if any)
        CurrentProgressHandler?.Report(step);

        // Notify all subscribers (they will handle UI thread marshaling)
        foreach (Action<SaveInitStep> handler in
                 _progressHandlers.ToList()) // ToList to avoid modification during iteration
            handler(step);
    }

    /// <summary>
    ///     Subscribe to progress updates
    /// </summary>
    public void SubscribeToProgress(Action<SaveInitStep> handler)
    {
        _progressHandlers.Add(handler);
    }

    /// <summary>
    ///     Unsubscribe from progress updates
    /// </summary>
    public void UnsubscribeFromProgress(Action<SaveInitStep> handler)
    {
        _progressHandlers.Remove(handler);
    }

    /// <summary>
    ///     Clear initialization state
    /// </summary>
    public void ClearInitialization()
    {
        IsInitializing = false;
        CurrentInitializingSave = null;
        InitSteps = null;
        CurrentProgressHandler = null;
        _progressHandlers.Clear();
    }
}
