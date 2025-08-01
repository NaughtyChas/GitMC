using System.Diagnostics;
using LibGit2Sharp;

namespace GitMC.Services;

public class GitService : IGitService
{
    private readonly string _initialDirectory;
    private readonly List<string> _directoryStack = [];
    private string _currentDirectory;

    public GitService()
    {
        _initialDirectory = _currentDirectory = Directory.GetCurrentDirectory();
    }

    public async Task<string> GetVersionAsync()
    {
        try
        {
            // Use LibGit2Sharp version first, fallback to command line
            var libgit2Version = LibGit2Sharp.GlobalSettings.Version;
            return await Task.FromResult($"LibGit2Sharp {libgit2Version} / Git {await GetSystemGitVersionAsync()}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred while getting Git version: {ex.Message}");
            return "LibGit2Sharp Available / Git " + await GetSystemGitVersionAsync();
        }
    }

    private async Task<string> GetSystemGitVersionAsync()
    {
        try
        {
            GitCommandResult result = await ExecuteCommandAsync("--version");
            if (result.Success && result.OutputLines.Length > 0)
            {
                string output = result.OutputLines[0];
                string[] parts = output.Split(' ');
                return parts.Length >= 3 ? parts[2] : "Unknown";
            }
        }
        catch
        {
            // Ignore errors
        }
        return "Not Found";
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
            await Task.Run(() =>
            {
                using var repo = new Repository(_currentDirectory);
                repo.Config.Set("user.name", userName);
                repo.Config.Set("user.email", userEmail);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<(string? userName, string? userEmail)> GetIdentityAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var repo = new Repository(_currentDirectory);
                var userName = repo.Config.Get<string>("user.name")?.Value;
                var userEmail = repo.Config.Get<string>("user.email")?.Value;
                return (userName, userEmail);
            });
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

    public string GetCurrentDirectory() => _currentDirectory;

    public string? GetPreviousDirectory() => _directoryStack.Count > 0 ? _directoryStack.Last() : null;

    public void PopDirectory()
    {
        if (_directoryStack.Count > 0)
            _directoryStack.RemoveAt(_directoryStack.Count - 1);
    }

    public bool ChangeToInitialDirectory() => ChangeDirectory(_initialDirectory);

    public bool ChangeDirectory(string path, bool recordToStack = true)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        string targetPath;

        if (path == "..")
        {
            DirectoryInfo? parent = Directory.GetParent(_currentDirectory);
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
            // Try setting directory using system method first for possible exceptions
            Directory.SetCurrentDirectory(targetPath);

            // Only when we managed to change to the new directory should we add the last to the stack.
            if (recordToStack && _directoryStack.LastOrDefault() != _currentDirectory)
                _directoryStack.Add(_currentDirectory);

            // Get directory again for a precise and unified path expression.
            _currentDirectory = Directory.GetCurrentDirectory();
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
                    ModifiedFiles = status.Where(s => s.State.HasFlag(FileStatus.ModifiedInWorkdir)).Select(s => s.FilePath).ToArray(),
                    StagedFiles = status.Where(s => s.State.HasFlag(FileStatus.ModifiedInIndex) || s.State.HasFlag(FileStatus.NewInIndex)).Select(s => s.FilePath).ToArray(),
                    UntrackedFiles = status.Where(s => s.State.HasFlag(FileStatus.NewInWorkdir)).Select(s => s.FilePath).ToArray(),
                    DeletedFiles = status.Where(s => s.State.HasFlag(FileStatus.DeletedFromWorkdir) || s.State.HasFlag(FileStatus.DeletedFromIndex)).Select(s => s.FilePath).ToArray()
                };

                // Get ahead/behind counts if tracking branch exists
                var trackingBranch = repo.Head.TrackedBranch;
                if (trackingBranch != null)
                {
                    var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, trackingBranch.Tip);
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

    public async Task<bool> StageFileAsync(string filePath, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                repo.Index.Add(filePath);
                repo.Index.Write();
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> StageAllAsync(string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                repo.Index.Add("*");
                repo.Index.Write();
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> UnstageFileAsync(string filePath, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                repo.Index.Remove(filePath);
                repo.Index.Write();
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> CommitAsync(string message, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                repo.Commit(message, signature, signature);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
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

    public async Task<bool> CreateBranchAsync(string branchName, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                repo.CreateBranch(branchName);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> CheckoutBranchAsync(string branchName, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);
                var branch = repo.Branches[branchName];
                if (branch != null)
                {
                    LibGit2Sharp.Commands.Checkout(repo, branch);
                }
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> PullAsync(string? remoteName = null, string? branchName = null, string? workingDirectory = null)
    {
        try
        {
            await Task.Run(() =>
            {
                var repoPath = workingDirectory ?? _currentDirectory;
                using var repo = new Repository(repoPath);

                var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                var pullOptions = new PullOptions();

                LibGit2Sharp.Commands.Pull(repo, signature, pullOptions);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    public async Task<bool> PushAsync(string? remoteName = null, string? branchName = null, string? workingDirectory = null)
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
                    LibGit2Sharp.Commands.Fetch(repo, remote.Name, refSpecs, null, null);
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
                if (branch != null)
                {
                    repo.Merge(branch, signature);
                }
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
                if (commit != null)
                {
                    repo.Reset(resetMode, commit);
                }
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
                if (!string.IsNullOrEmpty(branchName))
                {
                    cloneOptions.BranchName = branchName;
                }

                Repository.Clone(url, targetPath, cloneOptions);
            });
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }
}
