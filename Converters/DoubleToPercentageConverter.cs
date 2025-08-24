using Microsoft.UI.Xaml.Data;

namespace GitMC.Converters
{
    public class DoubleToPercentageConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            if (value is double doubleValue)
            {
                return $"{doubleValue:F0}%";
            }
            return "0%";
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        {
            // One-way converter
            return 0.0;
        }
    }
}
