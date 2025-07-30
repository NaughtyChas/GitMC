using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GitMC.Helpers
{
    /// <summary>
    /// Helper class for creating and displaying common UI flyouts
    /// Centralizes flyout creation logic to improve maintainability
    /// </summary>
    public static class FlyoutHelper
    {
        /// <summary>
        /// Shows an error flyout with standardized styling
        /// </summary>
        /// <param name="anchor">Element to anchor the flyout to</param>
        /// <param name="title">Error title</param>
        /// <param name="message">Error message</param>
        public static void ShowErrorFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var flyout = CreateStandardFlyout(
                title,
                message,
                "\uE783", // Error icon
                ColorHelper.FromArgb(255, 209, 52, 56), // Red
                "OK"
            );

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Shows a success flyout with standardized styling
        /// </summary>
        /// <param name="anchor">Element to anchor the flyout to</param>
        /// <param name="title">Success title</param>
        /// <param name="message">Success message</param>
        public static void ShowSuccessFlyout(FrameworkElement? anchor, string title, string message)
        {
            if (anchor == null) return;

            var flyout = CreateStandardFlyout(
                title,
                message,
                "\uE73E", // Checkmark icon
                ColorHelper.FromArgb(255, 16, 124, 16), // Green
                "OK"
            );

            flyout.ShowAt(anchor);
        }

        /// <summary>
        /// Shows a confirmation flyout and returns the user's choice
        /// </summary>
        /// <param name="anchor">Element to anchor the flyout to</param>
        /// <param name="title">Confirmation title</param>
        /// <param name="message">Confirmation message</param>
        /// <param name="confirmText">Text for confirm button</param>
        /// <param name="cancelText">Text for cancel button</param>
        /// <returns>True if user confirmed, false if cancelled</returns>
        public static Task<bool> ShowConfirmationFlyout(FrameworkElement? anchor, string title, string message,
            string confirmText = "Continue", string cancelText = "Cancel")
        {
            if (anchor == null)
                return Task.FromResult(false);

            var tcs = new TaskCompletionSource<bool>();

            var confirmButton = new Button
            {
                Content = confirmText,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0),
                Style = Application.Current.Resources["AccentButtonStyle"] as Style
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        CreateFlyoutHeader(title, "\uE7BA", ColorHelper.FromArgb(255, 0, 120, 212)), // Info icon, blue
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        buttonPanel
                    }
                },
                Placement = FlyoutPlacementMode.Right
            };

            confirmButton.Click += (s, e) =>
            {
                flyout.Hide();
                tcs.SetResult(true);
            };

            cancelButton.Click += (s, e) =>
            {
                flyout.Hide();
                tcs.SetResult(false);
            };

            flyout.Closed += (s, e) =>
            {
                if (!tcs.Task.IsCompleted)
                    tcs.SetResult(false);
            };

            flyout.ShowAt(anchor);
            return tcs.Task;
        }

        /// <summary>
        /// Creates a standard flyout with consistent styling
        /// </summary>
        private static Flyout CreateStandardFlyout(string title, string message, string iconGlyph,
            Windows.UI.Color iconColor, string buttonText)
        {
            var okButton = new Button
            {
                Content = buttonText,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };

            var flyout = new Flyout
            {
                Content = new StackPanel
                {
                    Width = 300,
                    Children =
                    {
                        CreateFlyoutHeader(title, iconGlyph, iconColor),
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 107, 107, 107)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        okButton
                    }
                },
                Placement = FlyoutPlacementMode.Right
            };

            okButton.Click += (s, e) => flyout.Hide();
            return flyout;
        }

        /// <summary>
        /// Creates a standardized flyout header with icon and title
        /// </summary>
        private static StackPanel CreateFlyoutHeader(string title, string iconGlyph, Windows.UI.Color iconColor)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
                Children =
                {
                    new FontIcon
                    {
                        Glyph = iconGlyph,
                        FontSize = 16,
                        Foreground = new SolidColorBrush(iconColor),
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        FontSize = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
        }
    }
}
