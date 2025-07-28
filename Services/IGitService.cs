namespace GitMC.Services
{
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
        bool ChangeDirectory(string path);
    }

    public class GitCommandResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string[] OutputLines { get; set; } = Array.Empty<string>();
        public string[] ErrorLines { get; set; } = Array.Empty<string>();
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
