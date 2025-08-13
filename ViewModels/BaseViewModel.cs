using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitMC.ViewModels;

/// <summary>
///     Base ViewModel class
/// </summary>
public abstract class BaseViewModel : INotifyPropertyChanged
{
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