using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GitMC.Utils.UI;

/// <summary>
///     UI component factory
/// </summary>
public static class UiComponentFactory
{
    /// <summary>
    ///     Creates info panel
    /// </summary>
    public static StackPanel CreateInfoPanel(string? iconGlyph, string? iconPath, string title, string data,
        Color iconColor)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };

        // Add icon based on which parameter is provided
        if (!string.IsNullOrEmpty(iconPath))
        {
            if (iconPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var imageIcon = new ImageIcon
                {
                    Source = new SvgImageSource(new Uri($"ms-appx:///{iconPath.TrimStart('/', '.')}")),
                    Foreground = new SolidColorBrush(iconColor),
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    UseLayoutRounding = true
                };
                panel.Children.Add(imageIcon);
            }
            else
            {
                var bitmapIcon = new BitmapIcon
                {
                    UriSource = new Uri($"ms-appx:///{iconPath.TrimStart('/', '.')}"),
                    Foreground = new SolidColorBrush(iconColor),
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    UseLayoutRounding = true
                };
                panel.Children.Add(bitmapIcon);
            }
        }
        else if (!string.IsNullOrEmpty(iconGlyph))
        {
            var fontIcon = new FontIcon
            {
                Glyph = iconGlyph,
                Foreground = new SolidColorBrush(iconColor),
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = true
            };
            panel.Children.Add(fontIcon);
        }

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleText = new TextBlock
        {
            FontSize = 11,
            Text = title,
            Foreground = new SolidColorBrush(Colors.Gray),
            Margin = new Thickness(0, 0, 0, 2),
            UseLayoutRounding = true
        };

        var dataText = new TextBlock
        {
            FontSize = 13,
            Text = data,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Black),
            UseLayoutRounding = true
        };

        textPanel.Children.Add(titleText);
        textPanel.Children.Add(dataText);
        panel.Children.Add(textPanel);

        return panel;
    }

    /// <summary>
    ///     Creates Git status badge
    /// </summary>
    public static Border CreateGitStatusBadge(string iconGlyph, string text, Color color)
    {
        var badge = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 8, 0),
            UseLayoutRounding = true
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            Glyph = iconGlyph,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(icon);
        panel.Children.Add(textBlock);
        badge.Child = panel;

        return badge;
    }

    /// <summary>
    ///     Creates save card container
    /// </summary>
    public static Border CreateSaveCard()
    {
        return new Border
        {
            Background = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 225, 225, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            UseLayoutRounding = true
        };
    }

    /// <summary>
    ///     Creates accent button
    /// </summary>
    public static Button CreateAccentButton(string content, double height = 32, double minWidth = 60)
    {
        return new Button
        {
            Content = content,
            Height = height,
            MinWidth = minWidth,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };
    }
}
