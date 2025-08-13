using System.Text.Json;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
/// Service for managing GitMC manifest files according to initSave.md specification
/// </summary>
public class ManifestService : IManifestService
{
    private readonly IGitService _gitService;

    public ManifestService(IGitService gitService)
    {
        _gitService = gitService;
    }

    /// <summary>
    /// Create initial manifest file with entries for all SNBT files
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <returns>Created manifest</returns>
    public async Task<GitMCManifest> CreateInitialManifestAsync(string gitMcPath)
    {
        var manifest = new GitMCManifest();

        // Find all SNBT files in GitMC directory
        var snbtFiles = Directory.GetFiles(gitMcPath, "*.snbt", SearchOption.AllDirectories);

        foreach (var snbtFile in snbtFiles)
        {
            // Get relative path from GitMC directory
            var relativePath = Path.GetRelativePath(gitMcPath, snbtFile);
            // Normalize path separators to forward slashes for consistency
            relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');

            // Add entry with placeholder commit for initial manifest; will be updated after commit
            manifest.AddEntry(relativePath, "pending", deleted: false);
        }

        await SaveManifestAsync(gitMcPath, manifest);
        return manifest;
    }

    /// <summary>
    /// Load manifest from GitMC directory
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <returns>Loaded manifest or empty manifest if not found</returns>
    public async Task<GitMCManifest> LoadManifestAsync(string gitMcPath)
    {
        var manifestPath = Path.Combine(gitMcPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return new GitMCManifest();
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<GitMCManifest>(json);
            return manifest ?? new GitMCManifest();
        }
        catch
        {
            // If manifest is corrupted, return empty manifest
            return new GitMCManifest();
        }
    }

    /// <summary>
    /// Save manifest to GitMC directory
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="manifest">Manifest to save</param>
    public async Task SaveManifestAsync(string gitMcPath, GitMCManifest manifest)
    {
        var manifestPath = Path.Combine(gitMcPath, "manifest.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(manifest, options);
        await File.WriteAllTextAsync(manifestPath, json);
    }

    /// <summary>
    /// Update manifest for changed SNBT files
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="changedSnbtFiles">List of changed SNBT file paths</param>
    /// <param name="commitHash">Commit hash to assign (or "pending")</param>
    /// <returns>Updated manifest</returns>
    public async Task<GitMCManifest> UpdateManifestForChangesAsync(string gitMcPath, IEnumerable<string> changedSnbtFiles, string commitHash = "pending")
    {
        var manifest = await LoadManifestAsync(gitMcPath);

        foreach (var snbtFile in changedSnbtFiles)
        {
            // Normalize to manifest key relative to GitMC root and starting with 'region/'
            var normalizedPath = snbtFile.Replace(Path.DirectorySeparatorChar, '/');
            if (normalizedPath.StartsWith(gitMcPath.Replace(Path.DirectorySeparatorChar, '/') + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(gitMcPath.Replace(Path.DirectorySeparatorChar, '/').Length + 1);
            }
            // If it still contains leading './', trim it
            if (normalizedPath.StartsWith("./")) normalizedPath = normalizedPath.Substring(2);

            // Update or add entry
            manifest.AddEntry(normalizedPath, commitHash, deleted: false);
        }

        await SaveManifestAsync(gitMcPath, manifest);
        return manifest;
    }

    /// <summary>
    /// Update all "pending" entries in manifest with actual commit hash
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="actualCommitHash">Actual commit hash</param>
    /// <returns>Number of entries updated</returns>
    public async Task<int> UpdatePendingEntriesAsync(string gitMcPath, string actualCommitHash)
    {
        var manifest = await LoadManifestAsync(gitMcPath);
        var updatedCount = manifest.UpdatePendingEntries(actualCommitHash);

        if (updatedCount > 0)
        {
            await SaveManifestAsync(gitMcPath, manifest);
        }

        return updatedCount;
    }

    /// <summary>
    /// Get SNBT file content from a specific commit
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="snbtPath">Relative path to SNBT file</param>
    /// <param name="commitHash">Commit hash</param>
    /// <returns>SNBT content or null if not found</returns>
    public async Task<string?> GetSnbtFileFromCommitAsync(string gitMcPath, string snbtPath, string commitHash)
    {
        try
        {
            // Use git show command to retrieve file from specific commit
            var command = $"show {commitHash}:{snbtPath}";
            var result = await _gitService.ExecuteCommandAsync(command, gitMcPath);

            if (result.Success)
            {
                return string.Join(Environment.NewLine, result.OutputLines);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reconstruct save at specific commit using manifest
    /// </summary>
    /// <param name="gitMcPath">Path to GitMC directory</param>
    /// <param name="targetCommit">Target commit to reconstruct</param>
    /// <param name="outputPath">Output path for reconstructed files</param>
    public async Task<bool> ReconstructSaveAtCommitAsync(string gitMcPath, string targetCommit, string outputPath)
    {
        try
        {
            // Load manifest content from the specified commit to get the correct view
            var manifestContent = await GetSnbtFileFromCommitAsync(gitMcPath, "manifest.json", targetCommit);
            GitMCManifest manifest;
            if (!string.IsNullOrEmpty(manifestContent))
            {
                manifest = JsonSerializer.Deserialize<GitMCManifest>(manifestContent!) ?? new GitMCManifest();
            }
            else
            {
                manifest = await LoadManifestAsync(gitMcPath);
            }
            var activeFiles = manifest.GetActiveSnbtFiles(targetCommit);

            // Create output directory
            Directory.CreateDirectory(outputPath);

            foreach (var snbtPath in activeFiles)
            {
                var entry = manifest[snbtPath];

                // Get file content from the commit where it was last modified
                var content = await GetSnbtFileFromCommitAsync(gitMcPath, snbtPath, entry.Commit);

                if (content != null)
                {
                    var outputFile = Path.Combine(outputPath, snbtPath);
                    var outputDir = Path.GetDirectoryName(outputFile);

                    if (outputDir != null)
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    await File.WriteAllTextAsync(outputFile, content);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
