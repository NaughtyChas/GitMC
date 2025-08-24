namespace GitMC.Services;

/// <summary>
/// Service interface for managing partial storage system operations
/// Handles ongoing commits with change detection and SNBT partial storage
/// </summary>
public interface IPartialStorageService
{
    /// <summary>
    /// Detects changed chunks compared to the last Git commit state
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <returns>List of changed MCA files and their chunk information</returns>
    Task<ChangeDetectionResult> DetectChangedChunksAsync(string savePath);

    /// <summary>
    /// Exports only changed chunks to SNBT format in GitMC directory
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="changedChunks">List of changed chunks to export</param>
    /// <param name="progress">Progress reporting for UI updates</param>
    /// <returns>List of exported SNBT files</returns>
    Task<ExportResult> ExportChangedChunksToSnbtAsync(string savePath, ChangeDetectionResult changedChunks, IProgress<PartialStorageProgress>? progress = null);

    /// <summary>
    /// Creates a commit with only changed SNBT files and updated manifest
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="exportedFiles">List of exported SNBT files</param>
    /// <param name="commitMessage">Commit message</param>
    /// <returns>Commit result with hash</returns>
    Task<CommitResult> CommitChangesAsync(string savePath, ExportResult exportedFiles, string commitMessage);

    /// <summary>
    /// Cleans up working directory by removing committed SNBT files but preserving folder structure
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="exportedFiles">List of files to clean up</param>
    /// <param name="progress">Progress reporting for UI updates</param>
    Task CleanupWorkingDirectoryAsync(string savePath, ExportResult exportedFiles, IProgress<PartialStorageProgress>? progress = null);

    /// <summary>
    /// Complete workflow: detect changes, export, commit, and cleanup
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="commitMessage">Commit message</param>
    /// <param name="cleanupAfterCommit">Whether to cleanup SNBT files after successful commit</param>
    /// <param name="progress">Progress reporting for UI updates</param>
    Task<CommitResult> PerformPartialCommitAsync(string savePath, string commitMessage, bool cleanupAfterCommit = true, IProgress<PartialStorageProgress>? progress = null);
}

/// <summary>
/// Result of change detection operation
/// </summary>
public class ChangeDetectionResult
{
    public List<ChangedMcaFile> ChangedFiles { get; set; } = new();
    public List<string> DeletedChunks { get; set; } = new();
    public bool HasChanges => ChangedFiles.Any() || DeletedChunks.Any();
}

/// <summary>
/// Information about a changed MCA file
/// </summary>
public class ChangedMcaFile
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<ChunkChange> ChunkChanges { get; set; } = new();
}

/// <summary>
/// Information about a specific chunk change
/// </summary>
public class ChunkChange
{
    public int ChunkX { get; set; }
    public int ChunkZ { get; set; }
    public ChangeType Type { get; set; }
    public string? PreviousHash { get; set; }
    public string? CurrentHash { get; set; }
}

/// <summary>
/// Type of change detected for a chunk
/// </summary>
public enum ChangeType
{
    Added,
    Modified,
    Deleted
}

/// <summary>
/// Result of SNBT export operation
/// </summary>
public class ExportResult
{
    public List<string> ExportedSnbtFiles { get; set; } = new();
    public List<string> DeletedChunks { get; set; } = new();
    public int TotalChunksProcessed { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of commit operation
/// </summary>
public class CommitResult
{
    public bool Success { get; set; }
    public string? CommitHash { get; set; }
    public string? ErrorMessage { get; set; }
    public int FilesCommitted { get; set; }
}

/// <summary>
/// Progress reporting for partial storage operations
/// </summary>
public class PartialStorageProgress
{
    public string Operation { get; set; } = string.Empty;
    public string CurrentFile { get; set; } = string.Empty;
    public int CurrentProgress { get; set; }
    public int TotalProgress { get; set; }
    public string Message { get; set; } = string.Empty;
}
