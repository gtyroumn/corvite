using Corvite.Core.Hardware;

namespace Corvite.Tweaks.Tweaks;

public static class TweakRegistry
{
    /// <summary>Every tweak that has a real implementation. Order = apply order (plan first, reboot-bound last).</summary>
    public static IReadOnlyList<ITweak> CreateAll()
    {
        var list = new List<ITweak>
        {
            new PlanTweak(),
            new PowerValueTweak("park", "CPMINCORES", v => v >= 0.5 ? 100 : 0, i => i >= 100 ? 1 : 0),
            new PowerValueTweak("minst", "PROCTHROTTLEMIN", v => (int)Math.Round(v), i => i),
        };
        list.AddRange(RegTweakFactory.Create());
        list.Add(new SysMainTweak());
        list.Add(new TcpAutoTweak());
        list.Add(new DnsTweak());
        foreach (var id in new[] { "prio", "standby", "aggr", "tmp", "upd", "idx" }) list.Add(new SessionSettingTweak(id));
        list.Add(new TrimTweak());
        // Reboot-bound last so everything else is already applied if something goes wrong.
        var late = new[] { "timer", "hags", "fast" };
        return list.Where(t => !late.Contains(t.Id)).Concat(late.SelectMany(id => list.Where(t => t.Id == id))).ToList();
    }
}
