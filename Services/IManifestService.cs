using GitMC.Models;

namespace GitMC.Services;

/// <summary>
/// Service for managing GitMC manifest files according to initSave.md specification
/// </summary>
public interface IManifestService
{
    /// <summary>
    /// Create initial manifest file with entries for all SNBT files
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <returns>Created manifest</returns>
    Task<GitMCManifest> CreateInitialManifestAsync(string gitMcPath);

    /// <summary>
    /// Load manifest from GitMC directory
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <returns>Loaded manifest or empty manifest if not found</returns>
    Task<GitMCManifest> LoadManifestAsync(string gitMcPath);

    /// <summary>
    /// Save manifest to GitMC directory
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="manifest">Manifest to save</param>
    Task SaveManifestAsync(string gitMcPath, GitMCManifest manifest);

    /// <summary>
    /// Update manifest for changed SNBT files
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="changedSnbtFiles">List of changed SNBT file paths</param>
    /// <param name="commitHash">Commit hash to assign (or "pending")</param>
    /// <returns>Updated manifest</returns>
    Task<GitMCManifest> UpdateManifestForChangesAsync(string gitMcPath, IEnumerable<string> changedSnbtFiles, string commitHash = "pending");

    /// <summary>
    /// Update all "pending" entries in manifest with actual commit hash
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="actualCommitHash">Actual commit hash</param>
    /// <returns>Number of entries updated</returns>
    Task<int> UpdatePendingEntriesAsync(string gitMcPath, string actualCommitHash);

    /// <summary>
    /// Get SNBT file content from a specific commit
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="snbtPath">Relative path to SNBT file</param>
    /// <param name="commitHash">Commit hash</param>
    /// <returns>SNBT content or null if not found</returns>
    Task<string?> GetSnbtFileFromCommitAsync(string gitMcPath, string snbtPath, string commitHash);

    /// <summary>
    /// Reconstruct save at specific commit using manifest
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="targetCommit">Target commit to reconstruct</param>
    /// <param name="outputPath">Output path for reconstructed files</param>
    Task<bool> ReconstructSaveAtCommitAsync(string gitMcPath, string targetCommit, string outputPath);
}
