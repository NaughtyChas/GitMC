using System;
using System.ComponentModel;

namespace GitMC.Models;

public enum OperationType
{
    Initialize,
    Commit,
    Translate,
    Rebuild
}

public enum OperationStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled
}

public class OperationInfo : INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SavePath { get; init; } = string.Empty;
    public OperationType Type { get; init; }

    private OperationStatus _status = OperationStatus.Pending;
    public OperationStatus Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

    private int _current;
    public int CurrentStep { get => _current; set { _current = value; OnPropertyChanged(nameof(CurrentStep)); } }

    private int _total;
    public int TotalSteps { get => _total; set { _total = value; OnPropertyChanged(nameof(TotalSteps)); } }

    private string _message = string.Empty;
    public string Message { get => _message; set { _message = value; OnPropertyChanged(nameof(Message)); } }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Touch();
    }
}
