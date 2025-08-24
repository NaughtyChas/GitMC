using System.Text.Json.Serialization;

namespace GitMC.Models;

/// <summary>
/// Represents the GitMC manifest file that tracks SNBT files and their associated commits
/// Following the specification in initSave.md for partial storage
/// </summary>
public class GitMCManifest : Dictionary<string, GitMCManifestEntry>
{
    /// <summary>
    /// Create a new empty manifest
    /// </summary>
    public GitMCManifest() : base()
    {
    }

    /// <summary>
    /// Add or update a manifest entry for an SNBT file
    /// </summary>
    /// <param name="snbtPath">Relative path to the SNBT file from GitMC directory</param>
    /// <param name="commit">Commit hash (or "pending" for uncommitted changes)</param>
    /// <param name="deleted">Whether the chunk was deleted</param>
    public void AddEntry(string snbtPath, string commit, bool deleted = false)
    {
        this[snbtPath] = new GitMCManifestEntry
        {
            Commit = commit,
            Deleted = deleted
        };
    }

    /// <summary>
    /// Update all pending entries with the actual commit hash
    /// </summary>
    /// <param name="actualCommitHash">The actual commit hash to replace "pending" with</param>
    /// <returns>Number of entries updated</returns>
    public int UpdatePendingEntries(string actualCommitHash)
    {
        var updated = 0;
        foreach (var entry in this.Values.Where(e => e.Commit == "pending"))
        {
            entry.Commit = actualCommitHash;
            updated++;
        }
        return updated;
    }

    /// <summary>
    /// Get all SNBT files that are not deleted as of a specific commit
    /// </summary>
    /// <param name="targetCommit">Target commit to check against</param>
    /// <returns>List of SNBT paths that should exist at the target commit</returns>
    public List<string> GetActiveSnbtFiles(string targetCommit)
    {
        return this.Where(kvp => !kvp.Value.Deleted &&
                               string.Compare(kvp.Value.Commit, targetCommit, StringComparison.Ordinal) <= 0)
                   .Select(kvp => kvp.Key)
                   .ToList();
    }
}

/// <summary>
/// Represents an entry in the GitMC manifest
/// </summary>
public class GitMCManifestEntry
{
    /// <summary>
    /// The commit hash where this SNBT file was last modified
    /// Can be "pending" for uncommitted changes or "init" for initial commit
    /// </summary>
    [JsonPropertyName("commit")]
    public string Commit { get; set; } = string.Empty;

    /// <summary>
    /// Whether this chunk/file was deleted
    /// </summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; } = false;
}
