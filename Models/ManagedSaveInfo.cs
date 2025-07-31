using GitMC.Utils;

namespace GitMC.Models;

/// <summary>
///     Data model for managed save information
/// </summary>
public class ManagedSaveInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public string Branch { get; set; } = "main";
    public int CommitCount { get; set; }
    public bool HasPendingChanges { get; set; }
    public string WorldIcon { get; set; } = "🌍";
    public string GameVersion { get; set; } = "Unknown";
    public bool IsGitInitialized { get; set; }

    // Computed properties
    public string SizeFormatted => CommonHelpers.FormatFileSize(Size);
    public string LastModifiedFormatted => CommonHelpers.FormatRelativeTime(LastModified);
}
