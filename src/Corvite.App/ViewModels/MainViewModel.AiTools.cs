using System.Collections.ObjectModel;
using System.Text;
using Corvite.App.Localization;
using Corvite.App.Services;
using Corvite.Core.Ai;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>Corvi: chat commands, "why does it lag", the player's own games, game list update, hints on the Overview.</summary>
public partial class MainViewModel
{
    // ---------------------------------------------------------------- commands: apply / undo / discard / what changed
    private ChatMsg ApplyMsg(string lang, bool howTo)
    {
        string T(string k) => Loc.I.Get(k, lang);
        if (!HasChanges) return Tailed(T("ai.a.none"), lang);
        var text = howTo ? T("ai.n.apply") : string.Format(T("ai.a.pending"), ChangedCount, T($"tot.{ProfileEngine.RiskLevel(Risk)}"));
        return new ChatMsg(text, false, Chips: new[]
        {
            new ChatBtn(T("ai.a.btn"), () => ApplyCommand.Execute(null), true),
            new ChatBtn(T("ai.c.list"), () => Say(ChangesMsg(lang))),
            new ChatBtn(T("ai.r.btn"), () => { RevertCommand.Execute(null); Say(new ChatMsg(T("ai.r.done"), false)); })
        });
    }

    private ChatMsg RevertMsg(string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        if (!HasChanges) return new ChatMsg(T("ai.r.none"), false);
        return new ChatMsg(string.Format(T("ai.r.ask"), ChangedCount), false, Chips: new[]
        {
            new ChatBtn(T("ai.r.btn"), () => { RevertCommand.Execute(null); Say(new ChatMsg(T("ai.r.done"), false)); }, true)
        });
    }

    private ChatMsg UndoMsg(string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var chips = new List<ChatBtn>();
        if (CanUndo) chips.Add(new ChatBtn(T("ai.u.btn"), () => { UndoCommand.Execute(null); Say(new ChatMsg(T("ai.u.started"), false)); }, true));
        chips.Add(NavChip(Section.Overview, lang));
        return Tailed(T("ai.n.undo"), lang, chips.ToArray());
    }

    private string ValueText(TweakDef d, double v, string lang) =>
        d.IsToggle ? Loc.I.Get(v >= 0.5 ? "on" : "ai.v.off", lang) : Fmt(d, v, lang);

    /// <summary>What is waiting to be applied; when nothing is, what differs from the original settings.</summary>
    private ChatMsg ChangesMsg(string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var pend = Changed();
        List<(TweakDef D, double From, double To)> rows;
        string head;
        if (pend.Count > 0)
        {
            rows = pend.Select(d => (d, _a[d.Id], _s[d.Id])).ToList();
            head = string.Format(T("ai.l.pend"), pend.Count);
        }
        else
        {
            var diff = Visible.Where(d => !d.IsGuard && IsSupported(d.Id) && Math.Abs(_a[d.Id] - BaseVal(d)) > 1e-9).ToList();
            if (diff.Count == 0) return Tailed(T("ai.l.none"), lang);
            rows = diff.Select(d => (d, BaseVal(d), _a[d.Id])).ToList();
            head = string.Format(T("ai.l.diff"), diff.Count);
        }

        var sb = new StringBuilder(head);
        foreach (var r in rows.Take(8))
            sb.Append("\n• ").Append(T($"tw.{r.D.Id}.name")).Append(": ").Append(ValueText(r.D, r.From, lang)).Append(" → ").Append(ValueText(r.D, r.To, lang));
        if (rows.Count > 8) sb.Append('\n').Append(string.Format(T("ai.l.more"), rows.Count - 8));

        var chips = new List<ChatBtn>();
        if (pend.Count > 0)
        {
            chips.Add(new ChatBtn(T("ai.a.btn"), () => ApplyCommand.Execute(null), true));
            chips.Add(new ChatBtn(T("ai.r.btn"), () => { RevertCommand.Execute(null); Say(new ChatMsg(T("ai.r.done"), false)); }));
        }
        else if (CanUndo) chips.Add(new ChatBtn(T("ai.u.btn"), () => { UndoCommand.Execute(null); Say(new ChatMsg(T("ai.u.started"), false)); }));
        return new ChatMsg(sb.ToString(), false, Chips: chips);
    }

    // ---------------------------------------------------------------- the player's own games
    private static string? GenreOf(string x) =>
        System.Text.RegularExpressions.Regex.IsMatch(x, @"fps|шутер|стрел|shooter") ? "FPS"
        : System.Text.RegularExpressions.Regex.IsMatch(x, @"moba|моба") ? "MOBA"
        : System.Text.RegularExpressions.Regex.IsMatch(x, @"tps|третьего|третього|third|приключ|action|экшен|екшен") ? "TPS"
        : null;

    private ChatMsg AskGenre(string name, string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        if (name.Length < 2 || name.Length > 40) return new ChatMsg(T("ai.u.fail"), false);
        _pendingName = name;
        var chips = new List<ChatBtn>();
        foreach (var g in new[] { "FPS", "MOBA", "TPS" })
        {
            var label = T("ai.u.g." + g);
            var genre = g;
            chips.Add(new ChatBtn(label, () => Ask(label, () => AddGame(name, genre, lang))));
        }
        chips.Add(new ChatBtn(T("ai.u.x"), () => { _pendingName = null; Say(new ChatMsg(T("ai.u.cancel"), false)); }));
        return new ChatMsg(string.Format(T("ai.u.genre"), name), false, Chips: chips);
    }

    private ChatMsg AddGame(string name, string genre, string lang)
    {
        _pendingName = null;
        var g = GameDb.AddUser(name, genre);
        if (g == null) return new ChatMsg(Loc.I.Get("ai.u.fail", lang), false);
        var m = GameMsg(g, lang);
        return m with { Text = string.Format(Loc.I.Get("ai.u.added", lang), g.Id, genre) + "\n\n" + m.Text };
    }

    // ---------------------------------------------------------------- game list update (no new app release needed)
    private async Task UpdateGameListAsync()
    {
        if (!_settings.UpdateGameList) return;
        await GameListUpdater.UpdateAsync();
    }

    // ---------------------------------------------------------------- "why does it lag"
    private DiagInput DiagState(bool live)
    {
        var s = live ? LastSample : null;
        return new DiagInput(s?.CpuTemp, s?.CpuLoad, s?.GpuTemp, s?.GpuLoad, s?.RamUsedGb ?? 0, s?.RamTotalGb ?? 0,
            Info.Ram.Type, Info.Ram.Profile, Info.Disk.Length > 0, Info.DiskIsSsd,
            Platform == PlatformKind.Laptop, PowerInfo.OnBattery(), Info.Tier);
    }

    private int GenericPlanCount()
    {
        var g = GameDb.All.FirstOrDefault(e => e.Id == "FPS") ?? GameDb.All.FirstOrDefault();
        return g == null ? 0 : Advisor.PlanForGame(g, Info.Tier, Platform, CanTouch, id => _s[id]).Count;
    }

    private ChatMsg DiagMsg(string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var live = LastSample != null;
        var list = Diagnosis.Run(DiagState(live), live);
        var off = GenericPlanCount();
        if (off > 0) { list.Add(new Finding("tweaks_off", 2, off)); list = list.OrderByDescending(f => f.Severity).ToList(); }

        var chips = new List<ChatBtn>();
        if (list.Count == 0)
            return Tailed(T("ai.d.none") + (live ? "" : "\n" + T("ai.d.nolive")), lang);

        var sb = new StringBuilder(T("ai.d.head"));
        foreach (var f in list.Take(4))
        {
            sb.Append("\n• ").Append(string.Format(T("ai.d." + f.Id), f.Value));
            switch (f.Id)
            {
                case "cpu_hot": chips.Add(NavChip(Section.Cpu, lang)); break;
                case "gpu_hot": chips.Add(NavChip(Section.Gpu, lang)); break;
                case "ram_full": chips.Add(NavChip(Section.Background, lang)); break;
                case "ram_slow": chips.Add(NavChip(Section.RamTimings, lang)); break;
                case "tweaks_off": chips.Add(GameChip(lang, true)); break;
            }
        }
        sb.Append("\n\n").Append(T(live ? "ai.d.note" : "ai.d.nolive"));
        var uniq = chips.GroupBy(c => c.Text).Select(g => g.First()).ToList();
        return new ChatMsg(sb.ToString(), false, Chips: uniq);
    }

    // ---------------------------------------------------------------- quiet hints on the Overview
    public ObservableCollection<HintVm> BuildHints()
    {
        string T(string k) => Loc.I[k];
        var res = new ObservableCollection<HintVm>();
        var hidden = _settings.HiddenHints;

        void Add(string id, string text, string? action, Action? run)
        {
            if (hidden.Contains(id) || res.Count >= 3) return;
            res.Add(new HintVm(id, text, action, run, () =>
            {
                hidden.Add(id);
                SettingsStore.Save(_settings);
                var h = res.FirstOrDefault(x => x.Id == id);
                if (h != null) res.Remove(h);
            }));
        }

        var found = Diagnosis.Run(DiagState(false), includeLive: false).ToDictionary(f => f.Id);
        if (found.ContainsKey("ram_slow")) Add("ram_slow", T("ai.h.ram_slow"), T("ai.h.open"), () => Navigate(Section.RamTimings));
        if (found.ContainsKey("hdd")) Add("hdd", T("ai.h.hdd"), null, null);
        if (found.ContainsKey("battery")) Add("battery", T("ai.h.battery"), null, null);
        var off = GenericPlanCount();
        if (off > 0) Add("tweaks_off", string.Format(T("ai.h.tweaks_off"), off), T("ai.h.optimise"), () => AiOpenWith(() => AskGameMsg(Loc.I.Lang)));
        return res;
    }
}
