using Corvite.Core.Hardware;

namespace Corvite.Tweaks;

public readonly record struct TweakResult(bool Ok, string? Error = null)
{
    public static TweakResult Success => new(true);
    public static TweakResult Fail(string error) => new(false, error);
}

/// <summary>
/// One real system change (TZ 13). Values: toggles 0/1, sliders their numeric value.
/// Read() reports the CURRENT state of the machine, so the UI always starts from the truth.
/// </summary>
public interface ITweak
{
    string Id { get; }

    /// <summary>The change only takes effect after a restart (e.g. HAGS).</summary>
    bool RequiresReboot { get; }

    /// <summary>Current value, or null when the feature does not exist on this machine.</summary>
    double? Read();

    /// <summary>Opaque snapshot of the current system state, used for exact restore (baseline / safety copy).</summary>
    string Capture();

    TweakResult Apply(double value);

    /// <summary>Puts the system back exactly as described by a snapshot made by Capture().</summary>
    TweakResult Restore(string captured);
}

/// <summary>Tweaks that only make sense on some hardware (e.g. SysMain only on SSD).</summary>
public interface IConditionalTweak
{
    bool IsApplicable(HardwareInfo hw);
}
