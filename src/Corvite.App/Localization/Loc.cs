using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Windows.Data;

namespace Corvite.App.Localization;

/// <summary>
/// Live localization (TZ section 12). XAML: {Binding [key], Source={x:Static loc:Loc.I}}.
/// Languages: ru (neutral resx), uk, en. Switching raises PropertyChanged for the indexer, so
/// every bound text updates instantly without restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private static readonly ResourceManager Rm =
        new("Corvite.App.Resources.Strings", Assembly.GetExecutingAssembly());

    private CultureInfo _culture = new("ru");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    /// <summary>"ru", "uk" or "en".</summary>
    public string Lang => _culture.TwoLetterISOLanguageName;

    public string this[string key] => Rm.GetString(key, _culture) ?? key;

    public string Get(string key, string lang)
    {
        var c = new CultureInfo(lang);
        return Rm.GetString(key, c) ?? key;
    }

    public void Set(string lang)
    {
        if (lang == Lang) return;
        _culture = new CultureInfo(lang);
        CultureInfo.CurrentUICulture = _culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        LanguageChanged?.Invoke();
    }
}
