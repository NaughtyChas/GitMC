using Microsoft.UI.Xaml.Data;

namespace GitMC.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, string language)
        {
            return value != null;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, string language)
        {
            // One-way converter; pass-through to avoid nullable warnings if ever called.
            return value;
        }
    }
}
