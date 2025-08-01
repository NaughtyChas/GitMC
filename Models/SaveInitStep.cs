using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitMC.Models;

/// <summary>
/// Represents a step in the save initialization process
/// </summary>
public class SaveInitStep : INotifyPropertyChanged
{
    private SaveInitStepStatus _status = SaveInitStepStatus.Pending;
    private string _message = string.Empty;
    private int _currentProgress;
    private int _totalProgress;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CurrentProgress
    {
        get => _currentProgress;
        set
        {
            if (_currentProgress != value)
            {
                _currentProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public int TotalProgress
    {
        get => _totalProgress;
        set
        {
            if (_totalProgress != value)
            {
                _totalProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string ProgressText => TotalProgress > 0 ? $"({CurrentProgress}/{TotalProgress})" : string.Empty;
    public bool HasProgress => TotalProgress > 0;

    /// <summary>
    /// Display name that includes progress for "Extracting chunks" step when in progress
    /// </summary>
    public string DisplayName =>
        IsInProgress && HasProgress && Name == "Extracting chunks"
            ? $"{Name} {ProgressText}"
            : Name;

    public SaveInitStepStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsInProgress));
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            if (_message != value)
            {
                _message = value;
                OnPropertyChanged();
            }
        }
    }

    // Computed properties for UI binding
    public string StatusIcon => Status switch
    {
        SaveInitStepStatus.Completed => "\uE930", // CheckMark
        SaveInitStepStatus.InProgress => "\uF16A", // Refresh (will be animated)
        SaveInitStepStatus.Failed => "\uE783", // Error
        _ => "\uEA3A" // Circle (pending)
    };

    public string StatusColor => Status switch
    {
        SaveInitStepStatus.Completed => "#107C10", // Success green
        SaveInitStepStatus.InProgress => "#0078D4", // Primary blue
        SaveInitStepStatus.Failed => "#D13438", // Error red
        _ => "#605E5C" // Neutral gray
    };

    public bool IsCompleted => Status == SaveInitStepStatus.Completed;
    public bool IsInProgress => Status == SaveInitStepStatus.InProgress;
    public bool IsPending => Status == SaveInitStepStatus.Pending;
    public bool IsFailed => Status == SaveInitStepStatus.Failed;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Status of a save initialization step
/// </summary>
public enum SaveInitStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}
