using Windows.UI;
using Microsoft.UI;

namespace GitMC.Constants;

/// <summary>
///     Centralized color constants for consistent theming across the application
/// </summary>
internal static class ColorConstants
{
    // Status Colors
    public static readonly Color ErrorRed = ColorHelper.FromArgb(255, 209, 52, 56);
    public static readonly Color SuccessGreen = ColorHelper.FromArgb(255, 16, 124, 16);
    public static readonly Color InfoBlue = ColorHelper.FromArgb(255, 0, 120, 212);
    public static readonly Color WarningOrange = ColorHelper.FromArgb(255, 255, 185, 0);

    // Text Colors
    public static readonly Color SecondaryText = ColorHelper.FromArgb(255, 107, 107, 107);
    public static readonly Color PrimaryText = ColorHelper.FromArgb(255, 0, 0, 0);
    public static readonly Color DisabledText = ColorHelper.FromArgb(255, 130, 130, 130);

    // Background Colors
    public static readonly Color CardBackground = ColorHelper.FromArgb(255, 248, 248, 248);
    public static readonly Color HoverBackground = ColorHelper.FromArgb(255, 240, 240, 240);
    public static readonly Color SelectedBackground = ColorHelper.FromArgb(255, 230, 243, 255);

    // Border Colors
    public static readonly Color BorderDefault = ColorHelper.FromArgb(255, 200, 200, 200);
    public static readonly Color BorderHover = ColorHelper.FromArgb(255, 150, 150, 150);
    public static readonly Color BorderActive = ColorHelper.FromArgb(255, 0, 120, 212);

    // Special Colors
    public static readonly Color TransparentOverlay = ColorHelper.FromArgb(128, 0, 0, 0);
    public static readonly Color AccentColor = ColorHelper.FromArgb(255, 0, 120, 212);
    public static readonly Color WarningOrangeBright = ColorHelper.FromArgb(255, 255, 140, 0); // Bright warning orange

    public static readonly Color
        SuccessBackgroundLight = ColorHelper.FromArgb(255, 220, 255, 220); // Light success background

    // Badge Color Themes
    internal static class BadgeColors
    {
        // Info Badge (Blue)
        public static readonly Color InfoBackground = ColorHelper.FromArgb(255, 227, 242, 253);
        public static readonly Color InfoBorder = ColorHelper.FromArgb(255, 200, 230, 250);
        public static readonly Color InfoText = ColorHelper.FromArgb(255, 33, 150, 243);
        public static readonly Color InfoLightBackground = ColorHelper.FromArgb(50, 33, 150, 243);

        // Warning Badge (Orange/Yellow)
        public static readonly Color WarningBackground = ColorHelper.FromArgb(255, 255, 248, 197);
        public static readonly Color WarningBorder = ColorHelper.FromArgb(255, 238, 216, 136);
        public static readonly Color WarningText = ColorHelper.FromArgb(255, 211, 149, 0);
        public static readonly Color WarningLightBackground = ColorHelper.FromArgb(50, 211, 149, 0);

        // Success Badge (Green)
        public static readonly Color SuccessBackground = ColorHelper.FromArgb(255, 220, 252, 231);
        public static readonly Color SuccessBorder = ColorHelper.FromArgb(255, 187, 247, 208);
        public static readonly Color SuccessText = ColorHelper.FromArgb(255, 22, 163, 74);
        public static readonly Color SuccessLightBackground = ColorHelper.FromArgb(50, 22, 163, 74);

        // Error Badge (Red)
        public static readonly Color ErrorBackground = ColorHelper.FromArgb(255, 254, 226, 226);
        public static readonly Color ErrorBorder = ColorHelper.FromArgb(255, 252, 165, 165);
        public static readonly Color ErrorText = ColorHelper.FromArgb(255, 220, 38, 38);
        public static readonly Color ErrorLightBackground = ColorHelper.FromArgb(50, 220, 38, 38);

        // Git Badge (Blue variant)
        public static readonly Color GitBackground = ColorHelper.FromArgb(50, 78, 142, 246);
        public static readonly Color GitBorder = ColorHelper.FromArgb(255, 78, 142, 246);
        public static readonly Color GitText = ColorHelper.FromArgb(255, 78, 142, 246);
    }

    // Icon Container Colors
    internal static class IconColors
    {
        public static readonly Color FolderBackground = ColorHelper.FromArgb(255, 227, 242, 253);
        public static readonly Color FolderBorder = ColorHelper.FromArgb(255, 200, 230, 250);
        public static readonly Color FolderIcon = ColorHelper.FromArgb(255, 33, 150, 243);
    }

    // Info Panel Colors
    internal static class InfoPanelColors
    {
        public static readonly Color SecondaryIconText = ColorHelper.FromArgb(255, 114, 114, 130);
        public static readonly Color SeparatorBackground = ColorHelper.FromArgb(255, 225, 225, 225);
    }
}

/// <summary>
///     Badge types for consistent styling across the application
/// </summary>
internal enum BadgeType
{
    Info,
    Warning,
    Success,
    Error,
    Git
}