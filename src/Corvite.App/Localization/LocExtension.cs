using System.Windows.Data;
using System.Windows.Markup;

namespace Corvite.App.Localization;

/// <summary>Shorthand: Text="{loc:T n_overview}" → live-updating binding to the Loc indexer.</summary>
[MarkupExtensionReturnType(typeof(BindingExpression))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; }

    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var b = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return b.ProvideValue(serviceProvider);
    }
}
