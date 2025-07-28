using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Data;
using GitMC.Services;

namespace GitMC.Extensions
{
    [MarkupExtensionReturnType(ReturnType = typeof(BindingBase))]
    public sealed class LocalizeExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        protected override object ProvideValue()
        {
            if (string.IsNullOrEmpty(Key))
                return new Binding();

            // Create a binding to the localization service
            return new Binding
            {
                Source = (App.Current as App)?.LocalizationService,
                Path = new Microsoft.UI.Xaml.PropertyPath($"[{Key}]"),
                Mode = BindingMode.OneWay
            };
        }
    }
}
