using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitMC.Models;

public class CommitInfo : INotifyPropertyChanged
{
    private string _author = string.Empty;
    private string _message = string.Empty;
    private string _sha = string.Empty;
    private string _timeAgo = string.Empty;
    private DateTime _timestamp;

    public string Sha
    {
        get => _sha;
        set
        {
            _sha = value;
            OnPropertyChanged();
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            OnPropertyChanged();
        }
    }

    public string Author
    {
        get => _author;
        set
        {
            _author = value;
            OnPropertyChanged();
        }
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set
        {
            _timestamp = value;
            OnPropertyChanged();
            UpdateTimeAgo();
        }
    }

    public string TimeAgo
    {
        get => _timeAgo;
        private set
        {
            _timeAgo = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void UpdateTimeAgo()
    {
        var timeDiff = DateTime.Now - Timestamp;

        if (timeDiff.TotalMinutes < 1)
            TimeAgo = "Just now";
        else if (timeDiff.TotalMinutes < 60)
            TimeAgo = $"{(int)timeDiff.TotalMinutes} min ago";
        else if (timeDiff.TotalHours < 24)
            TimeAgo = $"{(int)timeDiff.TotalHours}h ago";
        else if (timeDiff.TotalDays < 7)
            TimeAgo = $"{(int)timeDiff.TotalDays}d ago";
        else
            TimeAgo = Timestamp.ToString("MMM dd");
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}