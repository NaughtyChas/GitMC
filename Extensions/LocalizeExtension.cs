using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;

namespace GitMC.Extensions;

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
            Source = (Application.Current as App)?.LocalizationService,
            Path = new PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay
        };
    }
}
