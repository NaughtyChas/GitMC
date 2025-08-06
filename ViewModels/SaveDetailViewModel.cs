using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Models;

namespace GitMC.ViewModels;

public class SaveDetailViewModel : INotifyPropertyChanged
{
    private ManagedSaveInfo? _saveInfo;
    private bool _isLoading;
    private string _currentTab = "Overview";
    private ObservableCollection<CommitInfo> _recentCommits = new();
    private string _remoteUrl = string.Empty;

    public ManagedSaveInfo? SaveInfo
    {
        get => _saveInfo;
        set
        {
            _saveInfo = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string CurrentTab
    {
        get => _currentTab;
        set
        {
            _currentTab = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<CommitInfo> RecentCommits
    {
        get => _recentCommits;
        set
        {
            _recentCommits = value;
            OnPropertyChanged();
        }
    }

    public string RemoteUrl
    {
        get => _saveInfo?.GitHubRemoteUrl ?? _remoteUrl;
        set
        {
            _remoteUrl = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
