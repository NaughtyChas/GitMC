using System.Collections.ObjectModel;

namespace GitMC.Models;

/// <summary>
/// Represents a changed file in the version control system
/// </summary>
public class ChangedFile
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public ChangeStatus Status { get; set; }
    public FileCategory Category { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string DisplaySize { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string CategoryText { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE8A5"; // Default file icon
    public string StatusColor { get; set; } = "#2196F3";

    /// <summary>
    /// Indicates if this file has been translated to an editable SNBT representation
    /// according to the "one translation, then use" workflow. When true, the SNBT
    /// path should be available under the save's GitMC mirror folder.
    /// </summary>
    public bool IsTranslated { get; set; }

    /// <summary>
    /// If translated, holds the absolute path to the SNBT file to edit.
    /// </summary>
    public string? SnbtPath { get; set; }

    /// <summary>
    /// True when this file can be edited directly without translation (e.g., .txt, .json, .mcfunction).
    /// </summary>
    public bool IsDirectEditable { get; set; }

    /// <summary>
    /// The effective path the editor should load/save. For SNBT, this is SnbtPath; for direct-editable files, this is FullPath.
    /// Null when the file is not currently editable.
    /// </summary>
    public string? EditorPath { get; set; }

    /// <summary>
    /// For region files, contains the associated chunk files
    /// </summary>
    public ObservableCollection<ChunkInfo>? AssociatedChunks { get; set; }

    /// <summary>
    /// Number of chunks affected (for region files)
    /// </summary>
    public int ChunkCount { get; set; }
}

/// <summary>
/// Represents information about a chunk within a region file
/// </summary>
public class ChunkInfo
{
    public int ChunkX { get; set; }
    public int ChunkZ { get; set; }
    public string ChunkId => $"chunk_{ChunkX}_{ChunkZ}";
    public bool IsModified { get; set; }
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
    public long SizeBytes { get; set; }
    public string StatusText => IsDeleted ? "Deleted" : IsNew ? "New" : IsModified ? "Modified" : "Unchanged";
    public string StatusColor => IsDeleted ? "#F44336" : IsNew ? "#4CAF50" : IsModified ? "#FF9800" : "#9E9E9E";
}

/// <summary>
/// Groups changed files by category
/// </summary>
public class ChangedFileGroup
{
    public FileCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public string DisplaySize { get; set; } = string.Empty;
    public ObservableCollection<ChangedFile> Files { get; set; } = new();
    public bool IsExpanded { get; set; } = false; // Default to collapsed for better UI performance
}

/// <summary>
/// Status of a changed file
/// </summary>
public enum ChangeStatus
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked
}

/// <summary>
/// Category of file types
/// </summary>
public enum FileCategory
{
    Region,     // .mca files
    Data,       // level.dat, data/ folder contents
    Entity,     // entities/ folder contents
    Mod,        // mod-related files
    Other       // everything else
}
