using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Models;
using GitMC.Utils;

namespace GitMC.ViewModels;

public class SaveManagementViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private ObservableCollection<ManagedSaveInfo> _managedSaves = new();
    private string _saveCountText = "No saves managed yet";
    private string _totalSizeText = "0 B";

    public ObservableCollection<ManagedSaveInfo> ManagedSaves
    {
        get => _managedSaves;
        set
        {
            _managedSaves = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SaveCount));
            UpdateSaveCountText();
            UpdateTotalSizeText();
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

    public string TotalSizeText
    {
        get => _totalSizeText;
        set
        {
            _totalSizeText = value;
            OnPropertyChanged();
        }
    }

    public int SaveCount => ManagedSaves.Count;

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
        foreach (ManagedSaveInfo save in saves) ManagedSaves.Add(save);
        OnPropertyChanged(nameof(SaveCount));
        UpdateSaveCountText();
        UpdateTotalSizeText();
    }

    private void UpdateSaveCountText()
    {
        if (ManagedSaves.Count == 0)
            SaveCountText = "No saves managed yet";
        else
            SaveCountText = $"{ManagedSaves.Count} save{(ManagedSaves.Count == 1 ? "" : "s")} managed";
    }

    private void UpdateTotalSizeText()
    {
        long totalSize = ManagedSaves.Sum(save => save.Size);
        TotalSizeText = CommonHelpers.FormatFileSize(totalSize);
    }
}
