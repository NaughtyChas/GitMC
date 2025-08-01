namespace GitMC.Services;

public interface IGitService
{
    // Version and Installation
    Task<string> GetVersionAsync();
    Task<bool> IsInstalledAsync();

    // Command Execution
    Task<GitCommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null);

    // Repository Management
    Task<bool> InitializeRepositoryAsync(string path);
    Task<bool> IsRepositoryAsync(string path);

    // Configuration
    Task<bool> ConfigureIdentityAsync(string userName, string userEmail);
    Task<(string? userName, string? userEmail)> GetIdentityAsync();

    // Remote Operations
    Task<bool> AddRemoteAsync(string name, string url, string? workingDirectory = null);
    Task<string[]> GetRemotesAsync(string? workingDirectory = null);

    // Working Directory
    string GetCurrentDirectory();

    /// <summary>
    /// Get the previous directory in the directory stack.
    /// </summary>
    /// <returns>The previous directory if existing, otherwise null.</returns>
    string? GetPreviousDirectory();
    void PopDirectory();
    bool ChangeToInitialDirectory();
    bool ChangeDirectory(string path, bool recordToStack = true);

    // Enhanced Git Operations (LibGit2Sharp)
    Task<GitStatus> GetStatusAsync(string? workingDirectory = null);
    Task<GitOperationResult> StageFileAsync(string filePath, string? workingDirectory = null);
    Task<GitOperationResult> StageAllAsync(string? workingDirectory = null);
    Task<GitOperationResult> UnstageFileAsync(string filePath, string? workingDirectory = null);
    Task<GitOperationResult> CommitAsync(string message, string? workingDirectory = null);
    Task<GitCommit[]> GetCommitHistoryAsync(int count = 50, string? workingDirectory = null);
    Task<string[]> GetBranchesAsync(string? workingDirectory = null);
    Task<GitOperationResult> CreateBranchAsync(string branchName, string? workingDirectory = null);
    Task<GitOperationResult> CheckoutBranchAsync(string branchName, string? workingDirectory = null);
    Task<bool> PullAsync(string? remoteName = null, string? branchName = null, string? workingDirectory = null);
    Task<bool> PushAsync(string? remoteName = null, string? branchName = null, string? workingDirectory = null);
    Task<bool> FetchAsync(string? remoteName = null, string? workingDirectory = null);
    Task<bool> MergeAsync(string branchName, string? workingDirectory = null);
    Task<string> GetDiffAsync(string? filePath = null, string? workingDirectory = null);
    Task<bool> ResetAsync(string mode = "mixed", string? target = null, string? workingDirectory = null);
    Task<bool> CloneAsync(string url, string targetPath, string? branchName = null);
}

public class GitCommandResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string[] OutputLines { get; set; } = Array.Empty<string>();
    public string[] ErrorLines { get; set; } = Array.Empty<string>();
    public string ErrorMessage { get; set; } = string.Empty;
}

public class GitOperationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? WarningMessage { get; set; }

    public static GitOperationResult CreateSuccess(string? warning = null)
        => new() { Success = true, WarningMessage = warning };

    public static GitOperationResult CreateFailure(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}

public class GitStatus
{
    public string[] ModifiedFiles { get; set; } = Array.Empty<string>();
    public string[] UntrackedFiles { get; set; } = Array.Empty<string>();
    public string[] StagedFiles { get; set; } = Array.Empty<string>();
    public string[] DeletedFiles { get; set; } = Array.Empty<string>();
    public string CurrentBranch { get; set; } = string.Empty;
    public bool HasChanges => ModifiedFiles.Length > 0 || UntrackedFiles.Length > 0 || StagedFiles.Length > 0 || DeletedFiles.Length > 0;
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }
}

public class GitCommit
{
    public string Sha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public DateTime AuthorDate { get; set; }
    public string CommitterName { get; set; } = string.Empty;
    public string CommitterEmail { get; set; } = string.Empty;
    public DateTime CommitterDate { get; set; }
}
