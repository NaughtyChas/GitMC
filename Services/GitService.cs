using System.Diagnostics;
using LibGit2Sharp;

namespace GitMC.Services;

public class GitService : IGitService
{
    private readonly IConfigurationService _configurationService;
    private readonly List<string> _directoryStack = [];
    private readonly string _initialDirectory;
    private string _currentDirectory;

    public GitService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _initialDirectory = _currentDirectory = Directory.GetCurrentDirectory();
    }

    public async Task<string> GetVersionAsync()
    {
        try
        {
            // Use LibGit2Sharp version first, fallback to command line
            var libgit2Version = GlobalSettings.Version;
            return await Task.FromResult($"LibGit2Sharp {libgit2Version} / Git {await GetSystemGitVersionAsync()}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred while getting Git version: {ex.Message}");
            return "LibGit2Sharp Available / Git " + await GetSystemGitVersionAsync();
        }
    }

    public async Task<bool> IsInstalledAsync()
    {
        // LibGit2Sharp is always "installed" since it's embedded
        return await Task.FromResult(true);
    }

    public async Task<GitCommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null)
    {
        var result = new GitCommandResult();

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"--no-pager {(command.StartsWith("git ") ? command.Substring(4) : command)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? _currentDirectory
            };

            var outputLines = new List<string>();
            var errorLines = new List<string>();

            using var process = new Process { StartInfo = processInfo };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputLines.Add(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorLines.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            result.Success = process.ExitCode == 0;
            result.ExitCode = process.ExitCode;
            result.OutputLines = outputLines.ToArray();
            result.ErrorLines = errorLines.ToArray();

            if (!result.Success && errorLines.Count > 0)
                result.ErrorMessage = string.Join(Environment.NewLine, errorLines);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ExitCode = -1;
            result.ErrorMessage = ex.Message.Contains("cannot find") || ex.Message.Contains("not found")
                ? "Git is not installed or not found in PATH."
                : ex.Message;
        }

        return result;
    }

    public async Task<bool> InitializeRepositoryAsync(string path)
    {
        try
        {
            await Task.Run(() => Repository.Init(path));
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> IsRepositoryAsync(string path)
    {
        try
        {
            return await Task.Run(() => Repository.IsValid(path));
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ConfigureIdentityAsync(string userName, string userEmail)
    {
        try
        {
            // Store LibGit2Sharp identity in application configuration (not in system Git)
            _configurationService.DefaultGitUserName = userName;
            _configurationService.DefaultGitUserEmail = userEmail;
            await _configurationService.SaveAsync();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error configuring LibGit2Sharp identity: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> HasGitIdentityAsync()
    {
        try
        {
            (var userName, var userEmail) = await GetIdentityAsync();
            return !string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(userEmail);
        }
        catch
        {
            return false;
        }
    }

    public async Task<(string? userName, string? userEmail)> GetIdentityAsync()
    {
        try
        {
            // Get LibGit2Sharp identity from application configuration (not from system Git)
            var userName = _configurationService.DefaultGitUserName;
            var userEmail = _configurationService.DefaultGitUserEmail;

            return await Task.FromResult((
                string.IsNullOrEmpty(userName) ? null : userName,
                string.IsNullOrEmpty(userEmail) ? null : userEmail
            ));
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task<(string? userName, string? userEmail)> GetSystemGitIdentityAsync()
    {
        try
        {
            // Get system Git configuration using command line for migration purposes only
            var nameResult = await ExecuteCommandAsync("config --global user.name");
            var emailResult = await ExecuteCommandAsync("config --global user.email");

            var userName = nameResult.Success && nameResult.OutputLines.Length > 0
                ? nameResult.OutputLines[0].Trim()
                : null;
            var userEmail = emailResult.Success && emailResult.OutputLines.Length > 0
                ? emailResult.OutputLines[0].Trim()
                : null;

            return (userName, userEmail);
        }
        catch
        {
            return (null, null);
        }
    }

    public async Task<bool> AddRemoteAsync(string name, string url, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                repo.Network.Remotes.Add(name, url);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<string[]> GetRemotesAsync(string? workingDirectory = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                return repo.Network.Remotes
                    .Select(r => $"{r.Name}\t{r.Url} (fetch)")
                    .ToArray();
            });
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public string GetCurrentDirectory()
    {
        return _currentDirectory;
    }

    public string? GetPreviousDirectory()
    {
        return _directoryStack.Count > 0 ? _directoryStack.Last() : null;
    }

    public void PopDirectory()
    {
        if (_directoryStack.Count > 0)
            _directoryStack.RemoveAt(_directoryStack.Count - 1);
    }

    public bool ChangeToInitialDirectory()
    {
        return ChangeDirectory(_initialDirectory);
    }

    public bool ChangeDirectory(string path, bool recordToStack = true)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string targetPath;

        if (path == "..")
        {
            var parent = Directory.GetParent(_currentDirectory);
            if (parent == null) return false;
            targetPath = parent.FullName;
        }
        else if (Path.IsPathRooted(path))
        {
            targetPath = path;
        }
        else
        {
            targetPath = Path.GetFullPath(Path.Combine(_currentDirectory, path));
        }

        if (Directory.Exists(targetPath))
        {
            // Only when we managed to validate the directory should we add the current to the stack.
            if (recordToStack && _directoryStack.LastOrDefault() != _currentDirectory)
                _directoryStack.Add(_currentDirectory);

            // Set the internal directory state without affecting the process-wide working directory
            _currentDirectory = targetPath;
            return true;
        }

        return false;
    }

    // Enhanced Git Operations using LibGit2Sharp
    public async Task<GitStatus> GetStatusAsync(string? workingDirectory = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                var status = repo.RetrieveStatus();

                var gitStatus = new GitStatus
                {
                    CurrentBranch = repo.Head.FriendlyName,
                    ModifiedFiles =
                        status.Where(s => s.State.HasFlag(FileStatus.ModifiedInWorkdir)).Select(s => s.FilePath)
                            .ToArray(),
                    StagedFiles =
                        status.Where(s =>
                                s.State.HasFlag(FileStatus.ModifiedInIndex) || s.State.HasFlag(FileStatus.NewInIndex))
                            .Select(s => s.FilePath).ToArray(),
                    UntrackedFiles =
                        status.Where(s => s.State.HasFlag(FileStatus.NewInWorkdir)).Select(s => s.FilePath)
                            .ToArray(),
                    DeletedFiles =
                        status.Where(s =>
                            s.State.HasFlag(FileStatus.DeletedFromWorkdir) ||
                            s.State.HasFlag(FileStatus.DeletedFromIndex)).Select(s => s.FilePath).ToArray()
                };

                // Get ahead/behind counts if tracking branch exists
                var trackingBranch = repo.Head.TrackedBranch;
                if (trackingBranch != null)
                {
                    var divergence =
                        repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, trackingBranch.Tip);
                    gitStatus.AheadCount = divergence?.AheadBy ?? 0;
                    gitStatus.BehindCount = divergence?.BehindBy ?? 0;
                }

                return gitStatus;
            });
        }
        catch
        {
            return new GitStatus();
        }
    }

    public async Task<GitOperationResult> StageFileAsync(string filePath, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Check if file exists in working directory
                var fullPath = Path.IsPathRooted(filePath)
                    ? filePath
                    : Path.Combine(repoPath, filePath);

                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    throw new LibGit2SharpException($"pathspec '{filePath}' did not match any files");

                repo.Index.Add(filePath);
                repo.Index.Write();
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Failed to stage file '{filePath}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error staging file '{filePath}': {ex.Message}");
        }
    }

    public async Task<GitOperationResult> StageAllAsync(string? workingDirectory = null)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Check if there are any changes to stage
                var status = repo.RetrieveStatus();
                var filesToStage = status.Where(s => s.State.HasFlag(FileStatus.ModifiedInWorkdir) ||
                                                     s.State.HasFlag(FileStatus.NewInWorkdir) ||
                                                     s.State.HasFlag(FileStatus.DeletedFromWorkdir)).ToList();

                if (filesToStage.Count == 0)
                {
                    // For initial commits, this might be expected - check if repo is empty
                    if (!repo.Head.Commits.Any())
                        // This is a new repository with no commits, and no files to stage
                        // This could be valid if .gitignore excludes all files or if the directory is truly empty
                        return GitOperationResult.CreateSuccess("No files to stage in new repository");
                    return GitOperationResult.CreateSuccess("No files to stage");
                }

                Commands.Stage(repo, "*");
                repo.Index.Write();
                return GitOperationResult.CreateSuccess($"Staged {filesToStage.Count} files");
            });
            return result;
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Failed to stage all changes: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error staging all changes: {ex.Message}");
        }
    }

    public async Task<GitOperationResult> UnstageFileAsync(string filePath, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Check if file is staged
                var status = repo.RetrieveStatus();
                var fileStatus = status.FirstOrDefault(s => s.FilePath == filePath);
                if (fileStatus == null || (!fileStatus.State.HasFlag(FileStatus.ModifiedInIndex) &&
                                           !fileStatus.State.HasFlag(FileStatus.NewInIndex)))
                    throw new LibGit2SharpException($"No changes staged for file '{filePath}'");

                Commands.Unstage(repo, filePath);
                repo.Index.Write();
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Failed to unstage file '{filePath}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error unstaging file '{filePath}': {ex.Message}");
        }
    }

    public async Task<GitOperationResult> CommitAsync(string message, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Check if there are any staged changes
                var status = repo.RetrieveStatus();
                if (!status.Where(s =>
                            s.State.HasFlag(FileStatus.ModifiedInIndex) || s.State.HasFlag(FileStatus.NewInIndex))
                        .Any())
                    throw new LibGit2SharpException(
                        "No changes added to commit (use \"git add\" and/or \"git commit -a\")");

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                repo.Commit(message, signature, signature);
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Commit failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error during commit: {ex.Message}");
        }
    }

    public async Task<GitOperationResult> AmendLastCommitAsync(string? message = null, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Ensure there are staged changes to amend
                var status = repo.RetrieveStatus();
                var hasIndexChanges = status.Any(s => s.State.HasFlag(FileStatus.ModifiedInIndex) || s.State.HasFlag(FileStatus.NewInIndex) || s.State.HasFlag(FileStatus.DeletedFromIndex));
                if (!hasIndexChanges)
                    throw new LibGit2SharpException("No staged changes to amend");

                // Build signature and message
                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                var newMessage = message ?? repo.Head.Tip.Message;

                // Amend commit
                repo.Commit(newMessage, signature, signature, new CommitOptions { AmendPreviousCommit = true });
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Amend failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error during amend: {ex.Message}");
        }
    }

    public async Task<GitCommit[]> GetCommitHistoryAsync(int count = 50, string? workingDirectory = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                return repo.Head.Commits
                    .Take(count)
                    .Select(c => new GitCommit
                    {
                        Sha = c.Sha,
                        Message = c.MessageShort,
                        AuthorName = c.Author.Name,
                        AuthorEmail = c.Author.Email,
                        AuthorDate = c.Author.When.DateTime,
                        CommitterName = c.Committer.Name,
                        CommitterEmail = c.Committer.Email,
                        CommitterDate = c.Committer.When.DateTime
                    })
                    .ToArray();
            });
        }
        catch
        {
            return Array.Empty<GitCommit>();
        }
    }

    public async Task<string[]> GetBranchesAsync(string? workingDirectory = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                return repo.Branches
                    .Where(b => !b.IsRemote)
                    .Select(b => b.IsCurrentRepositoryHead ? $"* {b.FriendlyName}" : $"  {b.FriendlyName}")
                    .ToArray();
            });
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<GitOperationResult> CreateBranchAsync(string branchName, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                // Check if branch already exists
                if (repo.Branches[branchName] != null)
                    throw new LibGit2SharpException($"A branch named '{branchName}' already exists.");

                repo.CreateBranch(branchName);
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Failed to create branch '{branchName}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure($"Unexpected error creating branch '{branchName}': {ex.Message}");
        }
    }

    public async Task<GitOperationResult> CheckoutBranchAsync(string branchName, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var branch = repo.Branches[branchName];
                if (branch == null)
                    throw new LibGit2SharpException($"pathspec '{branchName}' did not match any file(s) known to git");

                // Check for uncommitted changes
                var status = repo.RetrieveStatus();
                if (status.IsDirty)
                    throw new LibGit2SharpException(
                        "Your local changes to the following files would be overwritten by checkout. Please commit your changes or stash them before you switch branches.");

                Commands.Checkout(repo, branch);
            });
            return GitOperationResult.CreateSuccess();
        }
        catch (LibGit2SharpException ex)
        {
            return GitOperationResult.CreateFailure($"Failed to checkout branch '{branchName}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return GitOperationResult.CreateFailure(
                $"Unexpected error checking out branch '{branchName}': {ex.Message}");
        }
    }

    public async Task<bool> PullAsync(string? remoteName = null, string? branchName = null,
        string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                var pullOptions = new PullOptions();

                Commands.Pull(repo, signature, pullOptions);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> PushAsync(string? remoteName = null, string? branchName = null,
        string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var remote = repo.Network.Remotes[remoteName ?? "origin"];
                if (remote != null)
                {
                    var pushRefSpec = $"refs/heads/{branchName ?? repo.Head.FriendlyName}";
                    repo.Network.Push(remote, pushRefSpec);
                }
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> FetchAsync(string? remoteName = null, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var remote = repo.Network.Remotes[remoteName ?? "origin"];
                if (remote != null)
                {
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                    Commands.Fetch(repo, remote.Name, refSpecs, null, null);
                }
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> MergeAsync(string branchName, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                var branch = repo.Branches[branchName];
                if (branch != null) repo.Merge(branch, signature);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<string> GetDiffAsync(string? filePath = null, string? workingDirectory = null)
    {
        try
        {
            return await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var diff = string.IsNullOrEmpty(filePath)
                    ? repo.Diff.Compare<Patch>()
                    : repo.Diff.Compare<Patch>(new[] { filePath });

                return diff.Content;
            });
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<bool> ResetAsync(string mode = "mixed", string? target = null, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var resetMode = mode.ToLower() switch
                {
                    "soft" => ResetMode.Soft,
                    "hard" => ResetMode.Hard,
                    _ => ResetMode.Mixed
                };

                var commit = string.IsNullOrEmpty(target) ? repo.Head.Tip : repo.Lookup<Commit>(target);
                if (commit != null) repo.Reset(resetMode, commit);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> CloneAsync(string url, string targetPath, string? branchName = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var cloneOptions = new CloneOptions();
                if (!string.IsNullOrEmpty(branchName)) cloneOptions.BranchName = branchName;

                Repository.Clone(url, targetPath, cloneOptions);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<string?> GetCurrentCommitHashAsync(string? workingDirectory = null)
    {
        try
        {
            var repoPath = workingDirectory ?? _currentDirectory;
            if (string.IsNullOrEmpty(repoPath))
                return null;

            return await Task.Run(() =>
            {
                using var repo = new Repository(repoPath);
                return repo.Head?.Tip?.Sha;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting current commit hash: {ex.Message}");
            return null;
        }
    }

    private async Task<string> GetSystemGitVersionAsync()
    {
        try
        {
            var result = await ExecuteCommandAsync("--version");
            if (result.Success && result.OutputLines.Length > 0)
            {
                var output = result.OutputLines[0];
                var parts = output.Split(' ');
                return parts.Length >= 3 ? parts[2] : "Unknown";
            }
        }
        catch
        {
            // Ignore errors
        }

        return "Not Found";
    }
}
