using Corvite.Core.Hardware;
using Corvite.Tweaks.Tweaks;

namespace Corvite.Tweaks;

public sealed record ApplyReport(
    IReadOnlyList<string> Done,
    IReadOnlyDictionary<string, string> Failed,
    bool NeedsReboot,
    IReadOnlyDictionary<string, string> Before);

public sealed class TweakEngine
{
    private readonly Dictionary<string, ITweak> _t;
    private readonly List<string> _order;

    public bool DryRun { get; }

    public TweakEngine(bool dryRun = false)
    {
        DryRun = dryRun;
        var all = TweakRegistry.CreateAll();
        _order = all.Select(t => t.Id).ToList();
        _t = all.ToDictionary(t => t.Id);
    }

    public bool Has(string id) => _t.ContainsKey(id);

    /// <summary>Implemented and (when conditional) applicable to this machine. Reads the current value on the way.</summary>
    public bool IsAvailable(string id, HardwareInfo hw)
    {
        if (!_t.TryGetValue(id, out var t)) return false;
        if (t is IConditionalTweak c && !c.IsApplicable(hw)) return false;
        return true;
    }

    public double? Read(string id)
    {
        if (!_t.TryGetValue(id, out var t)) return null;
        try { return t.Read(); }
        catch (Exception ex) { Log.Info($"read {id} failed: {ex.Message}"); return null; }
    }

    public Dictionary<string, double> ReadAll(HardwareInfo hw)
    {
        var d = new Dictionary<string, double>();
        foreach (var id in _order)
        {
            if (!IsAvailable(id, hw)) continue;
            var v = Read(id);
            if (v.HasValue) d[id] = v.Value;
        }
        return d;
    }

    public Dictionary<string, string> CaptureAll(IEnumerable<string> ids)
    {
        var d = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            if (!_t.TryGetValue(id, out var t)) continue;
            try { d[id] = t.Capture(); } catch (Exception ex) { Log.Info($"capture {id} failed: {ex.Message}"); }
        }
        return d;
    }

    public BaselineData CreateBaseline(HardwareInfo hw)
    {
        var values = ReadAll(hw);
        var b = new BaselineData
        {
            Created = DateTime.Now,
            Values = values,
            Snapshots = CaptureAll(values.Keys)
        };
        if (!DryRun) BaselineStore.Save(b);
        Log.Info($"baseline created: {b.Snapshots.Count} tweaks, dryRun={DryRun}");
        return b;
    }

    /// <summary>Applies the requested values in priority order. Each step is logged; a failure never stops the rest.</summary>
    public ApplyReport Apply(IReadOnlyDictionary<string, double> changes)
    {
        var ids = _order.Where(changes.ContainsKey).ToList();
        var before = CaptureAll(ids);
        var done = new List<string>();
        var failed = new Dictionary<string, string>();
        bool reboot = false;

        foreach (var id in ids)
        {
            var t = _t[id];
            var v = changes[id];
            if (DryRun) { Log.Info($"[dry-run] {id} -> {v}"); done.Add(id); continue; }
            TweakResult r;
            try { r = t.Apply(v); }
            catch (Exception ex) { r = TweakResult.Fail(ex.Message); }
            if (r.Ok) { Log.Info($"apply {id} -> {v}: ok"); done.Add(id); reboot |= t.RequiresReboot; }
            else { Log.Info($"apply {id} -> {v}: FAILED {r.Error}"); failed[id] = r.Error ?? "error"; }
        }
        return new ApplyReport(done, failed, reboot, before);
    }

    /// <summary>Restores the exact state captured earlier (undo or baseline). Returns ids that could not be restored.</summary>
    public Dictionary<string, string> Restore(IReadOnlyDictionary<string, string> snapshots)
    {
        var failed = new Dictionary<string, string>();
        foreach (var id in _order.Where(snapshots.ContainsKey))
        {
            if (DryRun) { Log.Info($"[dry-run] restore {id}"); continue; }
            TweakResult r;
            try { r = _t[id].Restore(snapshots[id]); }
            catch (Exception ex) { r = TweakResult.Fail(ex.Message); }
            Log.Info($"restore {id}: {(r.Ok ? "ok" : "FAILED " + r.Error)}");
            if (!r.Ok) failed[id] = r.Error ?? "error";
        }
        return failed;
    }
}
