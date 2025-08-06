using Microsoft.UI.Xaml.Data;

namespace GitMC.Converters;

public class LocalizationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string key && !string.IsNullOrEmpty(key))
            if (Application.Current is App app)
                return app.LocalizationService.GetLocalizedString(key);

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}