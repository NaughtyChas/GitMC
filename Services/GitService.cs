using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GitMC.Services
{
    public class GitService : IGitService
    {
        private string _currentDirectory;

        public GitService()
        {
            _currentDirectory = Directory.GetCurrentDirectory();
        }

        public async Task<string> GetVersionAsync()
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
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred while getting Git version: {ex.Message}");
            }

            return "Not Found";
        }

        public async Task<bool> IsInstalledAsync()
        {
            try
            {
                var result = await ExecuteCommandAsync("--version");
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<GitCommandResult> ExecuteCommandAsync(string command, string? workingDirectory = null)
        {
            var result = new GitCommandResult();
            
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = command.StartsWith("git ") ? command.Substring(4) : command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? _currentDirectory
                };

                var outputLines = new List<string>();
                var errorLines = new List<string>();

                using var process = new Process { StartInfo = processInfo };
                
                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputLines.Add(e.Data);
                };

                process.ErrorDataReceived += (s, e) =>
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
                {
                    result.ErrorMessage = string.Join(Environment.NewLine, errorLines);
                }
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
                var result = await ExecuteCommandAsync("init", path);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsRepositoryAsync(string path)
        {
            try
            {
                var result = await ExecuteCommandAsync("rev-parse --git-dir", path);
                return result.Success;
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
                var nameResult = await ExecuteCommandAsync($"config user.name \"{userName}\"");
                var emailResult = await ExecuteCommandAsync($"config user.email \"{userEmail}\"");
                
                return nameResult.Success && emailResult.Success;
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
                var nameResult = await ExecuteCommandAsync("config user.name");
                var emailResult = await ExecuteCommandAsync("config user.email");

                var userName = nameResult.Success && nameResult.OutputLines.Length > 0 
                    ? nameResult.OutputLines[0] : null;
                var userEmail = emailResult.Success && emailResult.OutputLines.Length > 0 
                    ? emailResult.OutputLines[0] : null;

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
                var result = await ExecuteCommandAsync($"remote add {name} {url}", workingDirectory);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string[]> GetRemotesAsync(string? workingDirectory = null)
        {
            try
            {
                var result = await ExecuteCommandAsync("remote -v", workingDirectory);
                return result.Success ? result.OutputLines : Array.Empty<string>();
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

        public bool ChangeDirectory(string path)
        {
            try
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
                    _currentDirectory = targetPath;
                    Directory.SetCurrentDirectory(_currentDirectory);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
