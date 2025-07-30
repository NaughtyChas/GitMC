using Microsoft.UI;
using Windows.UI;

namespace GitMC.Constants
{
    /// <summary>
    /// Centralized color constants for consistent theming across the application
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
    }
}
