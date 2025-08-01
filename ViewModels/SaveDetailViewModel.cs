using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Models;

namespace GitMC.ViewModels;

public class SaveDetailViewModel : INotifyPropertyChanged
{
    private ManagedSaveInfo? _saveInfo;
    private bool _isLoading;
    private string _currentTab = "Overview";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
