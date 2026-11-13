using Corvite.Core.Catalog;
using Corvite.Core.Engine;

namespace Corvite.Core.Ai;

/// <summary>One parameter the advisor wants to change: id, current value, proposed value.</summary>
public sealed record AdviceChange(TweakDef Def, double From, double To);

/// <summary>
/// Rules-based advisor (TZ section 10, fallback without a model). Pure logic: it only proposes values.
/// Validator: a proposal is never outside the allowed range of the platform, never lands in the red zone,
/// never touches a tweak the caller says is unavailable (locked / not for this hardware), never lowers a value the user already set higher.
/// </summary>
public static class Advisor
{
    /// <summary>Profile id recommended for the machine: performance tier 2+ → "game", otherwise "safe".</summary>
    public static string RecommendedProfile(int tier) => tier >= 2 ? "game" : "safe";

    /// <summary>Active (changed) parameters of the given state that sit in the red zone.</summary>
    public static List<TweakDef> RedParams(IReadOnlyDictionary<string, double> state, PlatformKind p, Func<TweakDef, bool> canTouch) =>
        TweakCatalog.All.Where(d => !d.IsGuard && canTouch(d) && ProfileEngine.IsActive(d, state[d.Id]) && ProfileEngine.IsRed(d, state[d.Id], p)).ToList();

    private static readonly string[] BaseToggles =
        { "plan", "gamemode", "dvr", "fso", "timer", "park", "nagle", "nthr", "upd", "idx", "focus", "standby", "tmp", "lowlat", "refl", "antil" };
    private static readonly string[] HeavyToggles = { "shader", "gpm", "kill" };

    /// <summary>Slider positions (0..1 of the effective range) for a game plan; zero means "leave alone".</summary>
    private static IEnumerable<(string Id, double Pos)> SliderPlan(int tier, PlatformKind p)
    {
        var laptop = p == PlatformKind.Laptop;
        var hi = tier >= 2 && !laptop;
        yield return ("prio", .5);
        yield return ("minst", .45);
        yield return ("bgcap", .4);
        yield return ("gpow", hi ? .3 : 0);
        yield return ("gcore", hi ? .2 : 0);
        yield return ("co", hi ? .2 : 0);
        yield return ("uvi", .2);
        yield return ("pl2", tier >= 3 && !laptop ? .3 : 0);
    }

    /// <summary>Changes that optimise the machine for a game. Empty when everything needed is already in place.</summary>
    public static List<AdviceChange> PlanForGame(Game g, int tier, PlatformKind p, Func<TweakDef, bool> canTouch, Func<string, double> current)
    {
        var res = new List<AdviceChange>();
        var byId = TweakCatalog.All.ToDictionary(d => d.Id);

        foreach (var id in BaseToggles.Concat(g.Heavy ? HeavyToggles : Array.Empty<string>()))
        {
            if (!byId.TryGetValue(id, out var d) || d.IsGuard || !d.IsToggle || !canTouch(d)) continue;
            if (d.Risk >= 2) continue;                       // red-zone toggles are never switched on by the advisor
            if (current(id) >= 0.5) continue;
            res.Add(new AdviceChange(d, current(id), 1));
        }

        foreach (var (id, pos) in SliderPlan(tier, p))
        {
            if (pos <= 0 || !byId.TryGetValue(id, out var d) || d.IsGuard || d.IsToggle || !canTouch(d)) continue;
            var v = Math.Min(ProfileEngine.ValueFromPosition(d, pos, p), ProfileEngine.EffectiveMax(d, p));
            if (ProfileEngine.IsRed(d, v, p)) continue;      // validator: never the red zone
            if (current(id) >= v - 1e-9) continue;           // equal or already higher: leave the user's value
            res.Add(new AdviceChange(d, current(id), v));
        }
        return res;
    }
}

/// <summary>Helpers for the advisor's explanations: expected gain and the safe headroom of a slider.</summary>
public static class AdvisorMath
{
    /// <summary>Expected FPS gain range (percent) of one tweak at the given value; sliders scale with their position.</summary>
    public static (double Lo, double Hi) GainOf(TweakDef d, double value, PlatformKind p)
    {
        var k = d.IsToggle ? 1 : ProfileEngine.PositionOf(d, value, p);
        return (d.FpsMin * k, d.FpsMax * k);
    }

    /// <summary>Highest slider value that still lies in a zone not above maxZone (0 green, 1 yellow).</summary>
    public static double TopOfZone(TweakDef d, PlatformKind p, int maxZone)
    {
        var max = ProfileEngine.EffectiveMax(d, p);
        var n = (int)Math.Round((max - d.Min) / d.Step);
        var best = d.Min;
        for (var i = 0; i <= n; i++)
        {
            var v = d.Min + i * d.Step;
            if (ProfileEngine.ZoneOf(ProfileEngine.PositionOf(d, v, p), p) <= maxZone) best = v;
        }
        return best;
    }
}
