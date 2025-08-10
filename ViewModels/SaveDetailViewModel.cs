using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GitMC.Models;

namespace GitMC.ViewModels;

public class SaveDetailViewModel : INotifyPropertyChanged
{
    private string _currentTab = "Overview";
    private bool _isLoading;
    private ObservableCollection<CommitInfo> _recentCommits = new();
    private string _remoteUrl = string.Empty;
    private ManagedSaveInfo? _saveInfo;
    private ObservableCollection<ChangedFileGroup> _changedFileGroups = new();
    private ChangedFile? _selectedChangedFile;
    private string _commitMessage = string.Empty;
    private string _commitDescription = string.Empty;
    private bool _isCommitInProgress;
    private string _commitProgressMessage = string.Empty;
    private bool _canTranslate;

    // Editor-related state for the Changes tab
    private string _fileContent = string.Empty;
    private bool _isFileModified;
    private string _editorLineCount = string.Empty;
    private string _editorCharCount = string.Empty;
    private string _currentCursorPosition = string.Empty;
    private string _editorLineBase = string.Empty;
    // Start with tools panel hidden to maximize editor space by default
    private bool _isSidePanelVisible = false;
    private bool _canSaveFile;
    private bool _canValidate;
    private bool _canFormat;
    private bool _canMinify;
    private bool _canForceTranslation;
    private string _selectedFontFamily = "Consolas";
    private double _editorFontSize = 13;
    private bool _canCommitAndPush;
    private bool _isSyntaxHighlightingEnabled;
    private bool _areLineNumbersVisible;
    private bool _isWordWrapEnabled;
    private ValidationStatusModel? _validationStatus;
    private bool _isSnbtContext;
    private bool _isJsonContext;
    private bool _isRegionMapVisible;

    public ManagedSaveInfo? SaveInfo
    {
        get => _saveInfo;
        set
        {
            _saveInfo = value;
            OnPropertyChanged();
            // Update dependent values
            CanCommitAndPush = (_saveInfo?.IsGitHubLinked == true) && !IsCommitInProgress;
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

    public ObservableCollection<ChangedFileGroup> ChangedFileGroups
    {
        get => _changedFileGroups;
        set
        {
            _changedFileGroups = value;
            OnPropertyChanged();
        }
    }

    public ChangedFile? SelectedChangedFile
    {
        get => _selectedChangedFile;
        set
        {
            _selectedChangedFile = value;
            OnPropertyChanged();
            // Update feature toggles based on selection
            CanForceTranslation = _selectedChangedFile != null
                                   && !_selectedChangedFile.IsTranslated
                                   && !(_selectedChangedFile?.IsDirectEditable ?? false)
                                   && (_selectedChangedFile?.IsTranslatable ?? false);
            // Determine context by extension of EditorPath (or SnbtPath)
            var path = _selectedChangedFile?.EditorPath ?? _selectedChangedFile?.SnbtPath ?? _selectedChangedFile?.FullPath;
            var ext = string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetExtension(path).ToLowerInvariant();
            IsSnbtContext = ext == ".snbt";
            IsJsonContext = ext == ".json";

            // Validation applies to SNBT and JSON
            CanValidate = IsSnbtContext || IsJsonContext;
            // Format/Minify apply to SNBT and JSON
            CanFormat = IsSnbtContext || IsJsonContext;
            CanMinify = IsSnbtContext || IsJsonContext;
            // Save allowed when editor has a path (translated SNBT or direct-editable) and content modified
            CanSaveFile = !string.IsNullOrEmpty(_selectedChangedFile?.EditorPath) && IsFileModified;
        }
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            _commitMessage = value;
            OnPropertyChanged();
        }
    }

    public string CommitDescription
    {
        get => _commitDescription;
        set
        {
            _commitDescription = value;
            OnPropertyChanged();
        }
    }

    public bool IsCommitInProgress
    {
        get => _isCommitInProgress;
        set
        {
            _isCommitInProgress = value;
            OnPropertyChanged();
            // Keep CanCommitAndPush in sync
            CanCommitAndPush = (_saveInfo?.IsGitHubLinked == true) && !_isCommitInProgress;
        }
    }

    public string CommitProgressMessage
    {
        get => _commitProgressMessage;
        set { _commitProgressMessage = value; OnPropertyChanged(); }
    }

    public bool CanTranslate
    {
        get => _canTranslate;
        set { _canTranslate = value; OnPropertyChanged(); }
    }

    public string FileContent
    {
        get => _fileContent;
        set
        {
            _fileContent = value;
            OnPropertyChanged();
        }
    }

    public bool IsFileModified
    {
        get => _isFileModified;
        set
        {
            _isFileModified = value;
            OnPropertyChanged();
            CanSaveFile = !string.IsNullOrEmpty(SelectedChangedFile?.EditorPath) && _isFileModified;
        }
    }

    public string EditorLineCount
    {
        get => _editorLineCount;
        set { _editorLineCount = value; OnPropertyChanged(); }
    }

    public string EditorCharCount
    {
        get => _editorCharCount;
        set { _editorCharCount = value; OnPropertyChanged(); }
    }

    public string CurrentCursorPosition
    {
        get => _currentCursorPosition;
        set { _currentCursorPosition = value; OnPropertyChanged(); }
    }

    public string EditorLineBase
    {
        get => _editorLineBase;
        set { _editorLineBase = value; OnPropertyChanged(); }
    }

    public bool IsRegionMapVisible
    {
        get => _isRegionMapVisible;
        set { _isRegionMapVisible = value; OnPropertyChanged(); }
    }

    public bool IsSidePanelVisible
    {
        get => _isSidePanelVisible;
        set { _isSidePanelVisible = value; OnPropertyChanged(); }
    }

    public bool CanSaveFile
    {
        get => _canSaveFile;
        set { _canSaveFile = value; OnPropertyChanged(); }
    }

    public bool CanValidate
    {
        get => _canValidate;
        set { _canValidate = value; OnPropertyChanged(); }
    }

    public bool CanFormat
    {
        get => _canFormat;
        set { _canFormat = value; OnPropertyChanged(); }
    }

    public bool CanMinify
    {
        get => _canMinify;
        set { _canMinify = value; OnPropertyChanged(); }
    }

    public bool CanForceTranslation
    {
        get => _canForceTranslation;
        set { _canForceTranslation = value; OnPropertyChanged(); }
    }

    public string SelectedFontFamily
    {
        get => _selectedFontFamily;
        set { _selectedFontFamily = value; OnPropertyChanged(); }
    }

    public double EditorFontSize
    {
        get => _editorFontSize;
        set { _editorFontSize = value; OnPropertyChanged(); }
    }

    // Commit & Push availability: requires linked repo and not committing
    public bool CanCommitAndPush
    {
        get => _canCommitAndPush;
        set { _canCommitAndPush = value; OnPropertyChanged(); }
    }

    public bool IsSyntaxHighlightingEnabled
    {
        get => _isSyntaxHighlightingEnabled;
        set { _isSyntaxHighlightingEnabled = value; OnPropertyChanged(); }
    }

    public bool AreLineNumbersVisible
    {
        get => _areLineNumbersVisible;
        set { _areLineNumbersVisible = value; OnPropertyChanged(); }
    }

    public bool IsWordWrapEnabled
    {
        get => _isWordWrapEnabled;
        set { _isWordWrapEnabled = value; OnPropertyChanged(); }
    }

    public ValidationStatusModel? ValidationStatus
    {
        get => _validationStatus;
        set { _validationStatus = value; OnPropertyChanged(); }
    }

    // Editor context helpers
    public bool IsSnbtContext
    {
        get => _isSnbtContext;
        set { _isSnbtContext = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTextContext)); }
    }

    public bool IsJsonContext
    {
        get => _isJsonContext;
        set { _isJsonContext = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTextContext)); }
    }

    public bool IsTextContext => !_isSnbtContext && !_isJsonContext;

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

public class ValidationStatusModel
{
    public string Message { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE73E"; // check icon
    public string BackgroundColor { get; set; } = "#E6F4EA";
    public string ForegroundColor { get; set; } = "#1E7E34";
}
