using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace GitMC.Converters;

/// <summary>
///     Converter that selects appropriate font family based on text content
///     Automatically uses Chinese font for Chinese characters
/// </summary>
public class FontFamilyConverter : IValueConverter
{
    private static readonly FontFamily DefaultFont = new("Segoe UI, Microsoft YaHei, 微软雅黑, SimSun, 宋体");
    private static readonly FontFamily ChineseFont = new("Microsoft YaHei, 微软雅黑, SimSun, 宋体, Segoe UI");

    // Regex to detect Chinese characters (CJK Unified Ideographs)
    private static readonly Regex ChineseCharPattern = new(@"[\u4e00-\u9fff]");

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string text && !string.IsNullOrEmpty(text))
            // If text contains Chinese characters, use Chinese font
            if (ChineseCharPattern.IsMatch(text))
                return ChineseFont;

        // Check current language setting
        if (language.StartsWith("zh")) return ChineseFont;

        return DefaultFont;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}