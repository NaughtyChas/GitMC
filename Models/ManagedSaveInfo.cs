using GitMC.Utils;
using GitMC.Constants;
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
    public string SizeFormatted => CommonHelpers.FormatFileSize(Size);
    public string LastModifiedFormatted => CommonHelpers.FormatRelativeTime(LastModified);

    // Status badge computed properties
    public string StatusText => CurrentStatus switch
    {
        SaveStatus.Clear => "clear",
        SaveStatus.Modified => "modified",
        SaveStatus.Conflict => "conflict",
        _ => "clear"
    };

    public SolidColorBrush StatusBadgeBackground => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBackground),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningBackground),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorBackground),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBackground)
    };

    public SolidColorBrush StatusBadgeBorder => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBorder),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningBorder),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorBorder),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessBorder)
    };

    public SolidColorBrush StatusBadgeText => CurrentStatus switch
    {
        SaveStatus.Clear => new SolidColorBrush(ColorConstants.BadgeColors.SuccessText),
        SaveStatus.Modified => new SolidColorBrush(ColorConstants.BadgeColors.WarningText),
        SaveStatus.Conflict => new SolidColorBrush(ColorConstants.BadgeColors.ErrorText),
        _ => new SolidColorBrush(ColorConstants.BadgeColors.SuccessText)
    };

    // Git status computed properties
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

    public bool ShowStatusTextOnly => IsGitInitialized && ConflictCount == 0 && PendingPushCount == 0 && PendingPullCount == 0;
    public bool ShowPushBadge => IsGitInitialized && PendingPushCount > 0;
    public bool ShowPullBadge => IsGitInitialized && PendingPullCount > 0;
    public bool ShowConflictBadge => IsGitInitialized && ConflictCount > 0;

    public string PushStatusText => PendingPushCount == 1 ? "1 to push" : $"{PendingPushCount} to push";
    public string PullStatusText => PendingPullCount == 1 ? "1 to pull" : $"{PendingPullCount} to pull";
    public string ConflictStatusText => ConflictCount == 1 ? "1 conflict" : $"{ConflictCount} conflicts";

    // Conflict badge properties (using warning style for conflicts)
    public SolidColorBrush ConflictBadgeBackground => new SolidColorBrush(ColorConstants.BadgeColors.WarningBackground);
    public SolidColorBrush ConflictBadgeText => new SolidColorBrush(ColorConstants.BadgeColors.WarningText);

    // Folder icon properties
    public SolidColorBrush FolderIconBackground => new SolidColorBrush(ColorConstants.IconColors.FolderBackground);
    public SolidColorBrush FolderIconBorder => new SolidColorBrush(ColorConstants.IconColors.FolderBorder);
    public SolidColorBrush FolderIconForeground => new SolidColorBrush(ColorConstants.IconColors.FolderIcon);

    // Separator properties
    public SolidColorBrush SeparatorBackground => new SolidColorBrush(ColorConstants.InfoPanelColors.SeparatorBackground);

    // Info panel properties
    public SolidColorBrush InfoIconColor => new SolidColorBrush(ColorConstants.InfoPanelColors.SecondaryIconText);
    public SolidColorBrush InfoTextColor => new SolidColorBrush(ColorConstants.InfoPanelColors.SecondaryIconText);
}
