using System.Text.Json.Serialization;
using GitMC.Constants;
using GitMC.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitMC.Models;

/// <summary>
///     Data model for managed save information
/// </summary>
public class ManagedSaveInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime AddedDate { get; set; }
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public string Branch { get; set; } = "main";
    public int CommitCount { get; set; }
    public bool HasPendingChanges { get; set; }
    public string WorldIcon { get; set; } = "🌍";
    public string GameVersion { get; set; } = "Unknown";
    public string GitRepository { get; set; } = string.Empty;
    public bool IsGitInitialized { get; set; }
    public int PendingPushCount { get; set; }
    public int PendingPullCount { get; set; }
    public int ConflictCount { get; set; }

    // Status badge properties
    public enum SaveStatus
    {
        Clear,
        Modified,
        Conflict
    }

    public SaveStatus CurrentStatus { get; set; } = SaveStatus.Clear;

    // Computed properties
    [JsonIgnore]
    public string SizeFormatted => CommonHelpers.FormatFileSize(Size);
    [JsonIgnore]
    public string LastModifiedFormatted => CommonHelpers.FormatRelativeTime(LastModified);

    // Status badge computed properties
    [JsonIgnore]
    public string StatusText => CurrentStatus switch
    {
        SaveStatus.Clear => "clear",
        SaveStatus.Modified => "modified",
        SaveStatus.Conflict => "conflict",
        _ => "clear"
    };

    [JsonIgnore]
    public SolidColorBrush StatusBadgeBackground => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBackground),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningBackground),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorBackground),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBackground)
    };

    [JsonIgnore]
    public SolidColorBrush StatusBadgeBorder => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBorder),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningBorder),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorBorder),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBorder)
    };

    [JsonIgnore]
    public SolidColorBrush StatusBadgeText => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessText),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningText),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorText),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessText)
    };

    // Git status computed properties
    [JsonIgnore]
    public string GitStatusDescription
    {
        get
        {
            if (!IsGitInitialized) return "Not initialized";
            if (ConflictCount > 0) return "Has conflicts";
            if (PendingPushCount == 0 && PendingPullCount == 0) return "Up to date";
            return "Pending changes";
        }
    }

    [JsonIgnore]
    public bool ShowStatusTextOnly => IsGitInitialized && ConflictCount == 0 && PendingPushCount == 0 && PendingPullCount == 0;
    [JsonIgnore]
    public bool ShowPushBadge => IsGitInitialized && PendingPushCount > 0;
    [JsonIgnore]
    public bool ShowPullBadge => IsGitInitialized && PendingPullCount > 0;
    [JsonIgnore]
    public bool ShowConflictBadge => IsGitInitialized && ConflictCount > 0;

    [JsonIgnore]
    public string PushStatusText => PendingPushCount == 1 ? "1 to push" : $"{PendingPushCount} to push";
    [JsonIgnore]
    public string PullStatusText => PendingPullCount == 1 ? "1 to pull" : $"{PendingPullCount} to pull";
    [JsonIgnore]
    public string ConflictStatusText => ConflictCount == 1 ? "1 conflict" : $"{ConflictCount} conflicts";

    // Conflict badge properties (using warning style for conflicts)
    [JsonIgnore]
    public SolidColorBrush ConflictBadgeBackground => new SolidColorBrush(ColorConstants.BadgeColors.WarningBackground);
    [JsonIgnore]
    public SolidColorBrush ConflictBadgeText => new SolidColorBrush(ColorConstants.BadgeColors.WarningText);

    // Folder icon properties
    [JsonIgnore]
    public SolidColorBrush FolderIconBackground => new SolidColorBrush(ColorConstants.IconColors.FolderBackground);
    [JsonIgnore]
    public SolidColorBrush FolderIconBorder => new SolidColorBrush(ColorConstants.IconColors.FolderBorder);
    [JsonIgnore]
    public SolidColorBrush FolderIconForeground => new SolidColorBrush(ColorConstants.IconColors.FolderIcon);

    // Separator properties
    [JsonIgnore]
    public SolidColorBrush SeparatorBackground => new SolidColorBrush(ColorConstants.InfoPanelColors.SeparatorBackground);

    // Info panel properties
    [JsonIgnore]
    public SolidColorBrush InfoIconColor => new SolidColorBrush(ColorConstants.InfoPanelColors.SecondaryIconText);
    [JsonIgnore]
    public SolidColorBrush InfoTextColor => new SolidColorBrush(ColorConstants.InfoPanelColors.SecondaryIconText);
}
