using System.Globalization;

namespace Corvite.Tweaks.Tweaks;

/// <summary>
/// "During the game" options. Nothing is written to Windows when they are applied: the choice is stored in state.json
/// and GameSession acts on it while a game runs, then undoes what it did.
/// </summary>
public static class SessionSettings
{
    public static double Get(string id, double def = 0) =>
        double.TryParse(StateStore.Get("s." + id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    public static void Set(string id, double v) => StateStore.Set("s." + id, v.ToString(CultureInfo.InvariantCulture));
}

internal sealed class SessionSettingTweak : ITweak
{
    public string Id { get; }
    public bool RequiresReboot => false;
    public SessionSettingTweak(string id) { Id = id; }

    public double? Read() => SessionSettings.Get(Id);
    public string Capture() => SessionSettings.Get(Id).ToString(CultureInfo.InvariantCulture);

    public TweakResult Apply(double value) { SessionSettings.Set(Id, value); return TweakResult.Success; }

    public TweakResult Restore(string captured)
    {
        if (double.TryParse(captured, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) SessionSettings.Set(Id, v);
        return TweakResult.Success;
    }
}
