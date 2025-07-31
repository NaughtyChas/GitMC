using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Utils;

namespace GitMC.Models;

public class MinecraftSave : INotifyPropertyChanged
{
    private string _gameVersion = string.Empty;
    private string _gitStatus = "Not initialized";
    private bool _isGitInitialized;
    private DateTime _lastCommit;
    private DateTime _lastPlayed;
    private string _name = string.Empty;
    private string _path = string.Empty;
    private int _pendingChanges;
    private string _worldIcon = "🌍";
    private long _worldSize;
    private string _worldType = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string WorldType
    {
        get => _worldType;
        set => SetProperty(ref _worldType, value);
    }

    public DateTime LastPlayed
    {
        get => _lastPlayed;
        set => SetProperty(ref _lastPlayed, value);
    }

    public DateTime LastCommit
    {
        get => _lastCommit;
        set => SetProperty(ref _lastCommit, value);
    }

    public long WorldSize
    {
        get => _worldSize;
        set => SetProperty(ref _worldSize, value);
    }

    public string WorldSizeFormatted => CommonHelpers.FormatFileSize(_worldSize);

    public string GitStatus
    {
        get => _gitStatus;
        set => SetProperty(ref _gitStatus, value);
    }

    public int PendingChanges
    {
        get => _pendingChanges;
        set => SetProperty(ref _pendingChanges, value);
    }

    public bool IsGitInitialized
    {
        get => _isGitInitialized;
        set => SetProperty(ref _isGitInitialized, value);
    }

    public string GameVersion
    {
        get => _gameVersion;
        set => SetProperty(ref _gameVersion, value);
    }

    public string WorldIcon
    {
        get => _worldIcon;
        set => SetProperty(ref _worldIcon, value);
    }

    public string LastPlayedFormatted =>
        _lastPlayed == DateTime.MinValue ? "Never" : CommonHelpers.FormatRelativeTime(_lastPlayed);

    public string LastCommitFormatted =>
        _lastCommit == DateTime.MinValue ? "No commits" : CommonHelpers.FormatRelativeTime(_lastCommit);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, newValue))
            return false;

        field = newValue;
        OnPropertyChanged(propertyName);
        return true;
    }
}
