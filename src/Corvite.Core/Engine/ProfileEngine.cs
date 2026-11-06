using Corvite.Core.Catalog;

namespace Corvite.Core.Engine;

/// <summary>
/// Pure calculation layer (TZ sections 7.2, 7.4, 8): visibility by hardware, laptop limits,
/// risk zones, profile presets, FPS gain estimate and overall risk. No UI, no system access.
/// Toggles are stored as 0/1, sliders as their real value.
/// </summary>
public static class ProfileEngine
{
    public const string Custom = "custom";
    public static readonly string[] ProfileIds = { "stock", "safe", "game", "max" };

    /// <summary>Sliders whose maximum is halved on laptops (TZ 7.4).</summary>
    private static readonly HashSet<string> OcIds = new()
    { "ppt", "boost", "tlim", "gpow", "gcore", "gmem", "gtemp", "co", "pl2", "uvi" };

    public static bool IsVisible(TweakDef d, HardwareProfile hw)
    {
        if (d.Section == Section.Gpu && hw.Gpu is GpuVendor.IntelArc or GpuVendor.Unknown) return false;
        return d.Vendor switch
        {
            Vendor.Any => true,
            Vendor.AmdCpu => hw.Cpu == CpuVendor.Amd,
            Vendor.IntelCpu => hw.Cpu == CpuVendor.Intel,
            Vendor.NvidiaGpu => hw.Gpu == GpuVendor.Nvidia,
            Vendor.AmdGpu => hw.Gpu == GpuVendor.Amd,
            _ => true
        };
    }

    /// <summary>Effective slider maximum: roughly halved for overclock sliders on laptops.</summary>
    public static double EffectiveMax(TweakDef d, PlatformKind p)
    {
        if (d.IsToggle || p != PlatformKind.Laptop || !OcIds.Contains(d.Id)) return d.Max;
        var span = Math.Floor((d.Max - d.Min) * 0.5 / d.Step) * d.Step;
        return d.Min + Math.Max(d.Step, span);
    }

    /// <summary>Slider position 0..1 within the effective range.</summary>
    public static double PositionOf(TweakDef d, double value, PlatformKind p)
    {
        var max = EffectiveMax(d, p);
        return max <= d.Min ? 0 : (value - d.Min) / (max - d.Min);
    }

    public static double ValueFromPosition(TweakDef d, double t, PlatformKind p)
    {
        var max = EffectiveMax(d, p);
        var n = Math.Round(t * (max - d.Min) / d.Step, MidpointRounding.AwayFromZero);
        return Math.Min(max, d.Min + n * d.Step);
    }

    /// <summary>0 = green, 1 = yellow, 2 = red. PC: 50/80 %, laptop: 35/65 %.</summary>
    public static int ZoneOf(double t, PlatformKind p)
    {
        var (a, b) = p == PlatformKind.Laptop ? (0.35, 0.65) : (0.5, 0.8);
        return t < a ? 0 : t < b ? 1 : 2;
    }

    public static bool IsActive(TweakDef d, double value) => d.IsToggle ? value >= 0.5 : value > d.Min;

    /// <summary>Profile preset for every tweak in the catalog (guard tweaks keep their defaults).</summary>
    public static Dictionary<string, double> Preset(string profile, PlatformKind p)
    {
        var laptop = p == PlatformKind.Laptop;
        double pos = profile switch
        {
            "safe" => laptop ? 0.15 : 0.20,
            "game" => laptop ? 0.30 : 0.50,
            "max" => laptop ? 0.55 : 0.85,
            _ => 0
        };
        var o = new Dictionary<string, double>();
        foreach (var d in TweakCatalog.All)
        {
            if (d.IsGuard) { o[d.Id] = d.IsToggle ? (d.DefaultOn ? 1 : 0) : d.DefaultValue; continue; }
            if (d.IsToggle)
            {
                bool on = profile switch { "stock" => false, "safe" => d.Risk == 0, "game" => d.Risk <= 1, _ => true };
                o[d.Id] = on ? 1 : 0;
            }
            else o[d.Id] = ValueFromPosition(d, pos, p);
        }
        return o;
    }

    /// <summary>Returns the id of the profile that exactly matches the state, or "custom".</summary>
    public static string DetectProfile(IReadOnlyDictionary<string, double> state, PlatformKind p)
    {
        foreach (var id in ProfileIds)
        {
            var pr = Preset(id, p);
            if (TweakCatalog.All.Where(d => !d.IsGuard).All(d => Math.Abs(pr[d.Id] - state[d.Id]) < 1e-9)) return id;
        }
        return Custom;
    }

    private static IEnumerable<TweakDef> Scored(HardwareProfile hw) =>
        TweakCatalog.All.Where(d => !d.IsGuard && IsVisible(d, hw));

    /// <summary>Estimated FPS gain range, smoothed by 20·(1−e^(−x/20)) so it stays below ~20 %.</summary>
    public static (double Lo, double Hi) Gain(IReadOnlyDictionary<string, double> s, HardwareProfile hw, PlatformKind p)
    {
        double lo = 0, hi = 0;
        foreach (var d in Scored(hw))
        {
            var v = s[d.Id];
            if (!IsActive(d, v)) continue;
            var k = d.IsToggle ? 1 : PositionOf(d, v, p);
            lo += d.FpsMin * k; hi += d.FpsMax * k;
        }
        static double F(double x) => 20 * (1 - Math.Exp(-x / 20));
        return (F(lo), F(hi));
    }

    /// <summary>Overall risk 0..1: 0.6·max + 0.4·mean of active params, ×1.3 on laptops.</summary>
    public static double RiskScore(IReadOnlyDictionary<string, double> s, HardwareProfile hw, PlatformKind p)
    {
        double mx = 0, sum = 0; int n = 0;
        foreach (var d in Scored(hw))
        {
            var v = s[d.Id];
            if (!IsActive(d, v)) continue;
            var r = d.IsToggle ? new[] { 0.12, 0.4, 0.8 }[d.Risk] : PositionOf(d, v, p);
            mx = Math.Max(mx, r); sum += r; n++;
        }
        if (n == 0) return 0;
        return Math.Min(1, (0.6 * mx + 0.4 * sum / n) * (p == PlatformKind.Laptop ? 1.3 : 1));
    }

    /// <summary>0..3 → resource keys tot.0..tot.3 (Low / Moderate / Elevated / High).</summary>
    public static int RiskLevel(double score) => score < .3 ? 0 : score < .55 ? 1 : score < .75 ? 2 : 3;

    /// <summary>Tweaks in the red zone among the given ids (used for the confirmation dialog and the AI advisor).</summary>
    public static bool IsRed(TweakDef d, double value, PlatformKind p) =>
        !d.IsGuard && (d.IsToggle ? value >= 0.5 && d.Risk == 2 : ZoneOf(PositionOf(d, value, p), p) == 2);
}
