using System.ComponentModel;

namespace GitMC.Services;

public interface IOnboardingService : INotifyPropertyChanged
{
    // Progress tracking
    bool IsOnboardingComplete { get; }
    int CurrentStepIndex { get; }
    OnboardingStepStatus[] StepStatuses { get; }

    // First launch detection
    bool IsFirstLaunch { get; }

    // Initialization
    Task InitializeAsync();

    // Step management
    Task<bool> CheckStepStatus(int stepIndex);
    Task CompleteStep(int stepIndex);
    Task MoveToNextStep();
    Task RefreshAllSteps();

    // Configuration management - both sync and async versions
    Task SetConfigurationValueAsync(string key, bool value);
    void SetConfigurationValue(string key, bool value); // Legacy compatibility

    Task RefreshApplicationDataCacheAsync();
    void RefreshApplicationDataCache(); // Legacy compatibility
    void MarkFirstLaunchComplete();
}

public enum OnboardingStepStatus
{
    NotStarted, // Grey - Future step
    Current, // Blue - Active step  
    Completed // Green - Finished step
}

public class OnboardingStep
{
    public string Title { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string FullDescription { get; set; } = string.Empty;
    public OnboardingStepStatus Status { get; set; }
    public Func<Task<bool>> StatusChecker { get; set; } = () => Task.FromResult(false);
}