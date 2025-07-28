using Microsoft.UI.Xaml.Markup;
using GitMC.Services;

namespace GitMC.Extensions
{
    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public sealed class LocalizeExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        protected override object ProvideValue()
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            // Get localization service from app
            if (App.Current is App app && app.LocalizationService != null)
            {
                return app.LocalizationService.GetLocalizedString(Key);
            }

            return Key; // Fallback to key if service not available
        }
    }
}
