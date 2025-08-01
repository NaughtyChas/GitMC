using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Models;

namespace GitMC.ViewModels;

public class SaveManagementViewModel : INotifyPropertyChanged
{
    private ObservableCollection<ManagedSaveInfo> _managedSaves = new();
    private string _saveCountText = "No saves managed yet";
    private bool _isLoading;

    public ObservableCollection<ManagedSaveInfo> ManagedSaves
    {
        get => _managedSaves;
        set
        {
            _managedSaves = value;
            OnPropertyChanged();
            UpdateSaveCountText();
        }
    }

    public string SaveCountText
    {
        get => _saveCountText;
        set
        {
            _saveCountText = value;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void UpdateManagedSaves(List<ManagedSaveInfo> saves)
    {
        ManagedSaves.Clear();
        foreach (var save in saves)
        {
            ManagedSaves.Add(save);
        }
        UpdateSaveCountText();
    }

    private void UpdateSaveCountText()
    {
        if (ManagedSaves.Count == 0)
            SaveCountText = "No saves managed yet";
        else
            SaveCountText = $"{ManagedSaves.Count} save{(ManagedSaves.Count == 1 ? "" : "s")} managed";
    }
}

