using System.Text.RegularExpressions;
using Corvite.Core.Hardware;

namespace Corvite.Tweaks.Tweaks;

internal static partial class Power
{
    public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public static string? ActiveGuid()
    {
        var r = Proc.Run("powercfg", "/getactivescheme");
        var m = GuidRx().Match(r.Out);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>Returns (ac, dc) of a setting of the given scheme. Locale independent: only hex values are parsed; AC is second to last, DC last.</summary>
    public static (int Ac, int Dc)? Query(string scheme, string sub, string setting)
    {
        var r = Proc.Run("powercfg", $"/query {scheme} {sub} {setting}");
        if (!r.Ok) return null;
        var hex = HexRx().Matches(r.Out).Select(m => Convert.ToInt32(m.Groups[1].Value, 16)).ToList();
        if (hex.Count < 2) return null;
        return (hex[^2], hex[^1]);
    }

    public static bool SetBoth(string scheme, string sub, string setting, int ac, int dc)
    {
        var a = Proc.Run("powercfg", $"/setacvalueindex {scheme} {sub} {setting} {ac}");
        var d = Proc.Run("powercfg", $"/setdcvalueindex {scheme} {sub} {setting} {dc}");
        var s = Proc.Run("powercfg", $"/setactive {scheme}");
        return a.Ok && d.Ok && s.Ok;
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRx();

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})")]
    private static partial Regex HexRx();
}

/// <summary>Ultimate Performance power plan. The previous plan is remembered and brought back on Off / Restore.</summary>
internal sealed class PlanTweak : ITweak
{
    public string Id => "plan";
    public bool RequiresReboot => false;

    public double? Read()
    {
        var g = Power.ActiveGuid();
        if (g == null) return null;
        // Ultimate is a duplicated scheme with its own GUID: recognise it by the remembered id or by the original GUID.
        var ours = StateStore.Get("plan.ours");
        return g == Power.Ultimate || (ours != null && g == ours) ? 1 : 0;
    }

    public string Capture() => Power.ActiveGuid() ?? "";

    public TweakResult Apply(double value)
    {
        if (value < 0.5)
        {
            var prev = StateStore.Get("plan.prev");
            return string.IsNullOrEmpty(prev) ? TweakResult.Success : Restore(prev);
        }
        var cur = Power.ActiveGuid();
        var ours = StateStore.Get("plan.ours");
        if (cur != null && cur != ours && cur != Power.Ultimate) StateStore.Set("plan.prev", cur);

        var r = Proc.Run("powercfg", $"/setactive {Power.Ultimate}");
        if (r.Ok) { StateStore.Set("plan.ours", Power.Ultimate); return TweakResult.Success; }

        // The scheme is hidden until it is duplicated once.
        var d = Proc.Run("powercfg", $"/duplicatescheme {Power.Ultimate}");
        var m = System.Text.RegularExpressions.Regex.Match(d.Out, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        if (!d.Ok || !m.Success) return TweakResult.Fail("powercfg: " + (d.Err + d.Out).Trim());
        var id = m.Value.ToLowerInvariant();
        r = Proc.Run("powercfg", $"/setactive {id}");
        if (!r.Ok) return TweakResult.Fail("powercfg: " + (r.Err + r.Out).Trim());
        StateStore.Set("plan.ours", id);
        return TweakResult.Success;
    }

    public TweakResult Restore(string captured)
    {
        if (string.IsNullOrWhiteSpace(captured)) return TweakResult.Success;
        var r = Proc.Run("powercfg", $"/setactive {captured}");
        return r.Ok ? TweakResult.Success : TweakResult.Fail("powercfg: " + (r.Err + r.Out).Trim());
    }
}

/// <summary>Setting inside the active power scheme (core parking, minimum processor state).</summary>
internal sealed class PowerValueTweak : ITweak
{
    private readonly string _setting;
    private readonly Func<double, int> _toIndex;
    private readonly Func<int, double> _fromIndex;
    public string Id { get; }
    public bool RequiresReboot => false;

    public PowerValueTweak(string id, string setting, Func<double, int> toIndex, Func<int, double> fromIndex)
    { Id = id; _setting = setting; _toIndex = toIndex; _fromIndex = fromIndex; }

    public double? Read()
    {
        var g = Power.ActiveGuid();
        if (g == null) return null;
        var q = Power.Query(g, "SUB_PROCESSOR", _setting);
        return q == null ? null : _fromIndex(q.Value.Ac);
    }

    public string Capture()
    {
        var g = Power.ActiveGuid();
        var q = g == null ? null : Power.Query(g, "SUB_PROCESSOR", _setting);
        return q == null || g == null ? "" : $"{g}|{q.Value.Ac}|{q.Value.Dc}";
    }

    public TweakResult Apply(double value)
    {
        var g = Power.ActiveGuid();
        if (g == null) return TweakResult.Fail("no active power scheme");
        var idx = _toIndex(value);
        return Power.SetBoth(g, "SUB_PROCESSOR", _setting, idx, idx) ? TweakResult.Success : TweakResult.Fail("powercfg failed");
    }

    public TweakResult Restore(string captured)
    {
        var p = captured.Split('|');
        if (p.Length != 3 || !int.TryParse(p[1], out var ac) || !int.TryParse(p[2], out var dc)) return TweakResult.Success;
        return Power.SetBoth(p[0], "SUB_PROCESSOR", _setting, ac, dc) ? TweakResult.Success : TweakResult.Fail("powercfg failed");
    }
}

/// <summary>SysMain service: ON = stopped and disabled. Offered on SSD only.</summary>
internal sealed class SysMainTweak : ITweak, IConditionalTweak
{
    private const string Key = @"SYSTEM\CurrentControlSet\Services\SysMain";
    public string Id => "sysmain";
    public bool RequiresReboot => false;

    public bool IsApplicable(HardwareInfo hw) => hw.DiskIsSsd;

    public double? Read()
    {
        if (!Reg.TryGet("HKLM", Key, "Start", out var v, out _)) return null;
        return Reg.Eq(v, 4) ? 1 : 0;
    }

    public string Capture() => Reg.ToJson(new[] { Reg.Snap("HKLM", Key, "Start") });

    public TweakResult Apply(double value)
    {
        try
        {
            if (value >= 0.5)
            {
                Reg.Set("HKLM", Key, "Start", 4, Microsoft.Win32.RegistryValueKind.DWord);
                Proc.Run("sc", "stop SysMain");
            }
            else
            {
                Reg.Set("HKLM", Key, "Start", 2, Microsoft.Win32.RegistryValueKind.DWord);
                Proc.Run("sc", "start SysMain");
            }
            return TweakResult.Success;
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Restore(string captured)
    {
        try
        {
            var snaps = Reg.FromJson(captured);
            foreach (var s in snaps) Reg.RestoreSnap(s);
            if (snaps.Count > 0 && snaps[0].E && snaps[0].V != "4") Proc.Run("sc", "start SysMain");
            return TweakResult.Success;
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }
}

/// <summary>TRIM: ON = delete notifications (TRIM) enabled for NTFS.</summary>
internal sealed class TrimTweak : ITweak, IConditionalTweak
{
    public string Id => "trim";
    public bool RequiresReboot => false;

    public bool IsApplicable(HardwareInfo hw) => hw.DiskIsSsd;

    public double? Read()
    {
        var r = Proc.Run("fsutil", "behavior query DisableDeleteNotify");
        if (!r.Ok) return null;
        // Output contains "NTFS DisableDeleteNotify = 0 ..." (text is localized, digit is not).
        var m = System.Text.RegularExpressions.Regex.Match(r.Out, @"=\s*(\d)");
        if (!m.Success) return null;
        return m.Groups[1].Value == "0" ? 1 : 0;
    }

    public string Capture() => (Read() ?? 1).ToString("0");

    public TweakResult Apply(double value)
    {
        var r = Proc.Run("fsutil", $"behavior set DisableDeleteNotify {(value >= 0.5 ? 0 : 1)}");
        return r.Ok ? TweakResult.Success : TweakResult.Fail("fsutil: " + (r.Err + r.Out).Trim());
    }

    public TweakResult Restore(string captured) => Apply(captured == "1" ? 1 : 0);
}
