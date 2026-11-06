using System.Windows.Media;

namespace Corvite.App.Theming;

/// <summary>Code-side palette (same values as Theme.xaml; TZ section 3).</summary>
public static class Palette
{
    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Brush Ok = B("#3ddc84");
    public static readonly Brush Lime = B("#a5d84a");
    public static readonly Brush Warn = B("#e9c93f");
    public static readonly Brush Hot = B("#f2873a");
    public static readonly Brush Stop = B("#f04b4b");
    public static readonly Brush AccentLight = B("#a259f0");
    public static readonly Brush Dim = B("#6f6985");

    /// <summary>0 green, 1 yellow, 2 red.</summary>
    public static Brush Risk(int zone) => zone <= 0 ? Ok : zone == 1 ? Warn : Stop;

    /// <summary>Overall-risk word colour: low / moderate / elevated / high.</summary>
    public static Brush Total(int level) => level switch { 0 => Ok, 1 => Lime, 2 => Hot, _ => Stop };
}
