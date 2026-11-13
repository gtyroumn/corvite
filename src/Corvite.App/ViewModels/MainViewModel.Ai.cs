using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Corvite.App.Localization;
using Corvite.Core.Ai;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>One chat bubble. An optional action button lets the advisor take the user somewhere in the app.</summary>
public sealed record ChatMsg(string Text, bool IsUser, string? ActionText = null, Action? Run = null)
{
    public bool HasAction => ActionText != null && Run != null;
    public ICommand? ActionCommand => Run == null ? null : new RelayCommand(Run);
}

/// <summary>
/// Corvi, the AI advisor (stage 7, rules-based; a local model can be added later behind the same entry point).
/// What it does: optimise for a game or genre (changes stay pending until "Apply"), explain any parameter,
/// show where things are in the app, assess risk. Validation lives in Corvite.Core.Ai.Advisor.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<ChatMsg> AiMessages { get; } = new();
    [ObservableProperty] private bool _aiOpen;
    [ObservableProperty] private string _aiInput = "";

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GreetRx = Rx(@"^\s*(привет|здравств|добр|hello|hi\b|hey|привіт|вітаю)");
    private static readonly Regex HelpRx = Rx(@"умеешь|можешь|что ты|кто ты|help|помо[гщ]|can you|what can|who are you|вмієш|можеш|що ти|хто ти|допом");
    private static readonly Regex ExplainRx = Rx(@"что такое|что это|что делает|зачем|для чего|объясни|расскажи|what is|what's|what does|explain|why|що таке|що це|що робить|навіщо|для чого|поясни|розкажи");
    private static readonly Regex WhereRx = Rx(@"где|куда|как (найти|открыть|включить|выключить|перейти|посмотреть|изменить)|where|how (do i|can i|to) |де |куди|як (знайти|відкрити|увімкнути|вимкнути|перейти|подивитись|змінити)");
    private static readonly Regex BackupRx = Rx(@"резерв|бэкап|бекап|backup|копи[яию]|исходн|вихідн|original|restore point|точк|відновлен|восстанов");
    private static readonly Regex UndoRx = Rx(@"откат|отмен|верн[уи]|undo|revert|скасув|поверн|відкат");
    private static readonly Regex ApplyRx = Rx(@"примен|apply|застос");
    private static readonly Regex PlatRx = Rx(@"ноутбук|laptop|платформ|platform|пк или");
    private static readonly Regex LockRx = Rx(@"заблок|серый|недоступн|locked|unavailable|нужен драйвер|потрібен драйвер|скоро");
    private static readonly Regex SessRx = Rx(@"сесси|session|автоматическ|automatic|автоматичн|во время игры|during the game|під час гри");
    private static readonly Regex RiskRx = Rx(@"риск|ризик|risk|опас|небезп|danger|безопасн|safe");
    private static readonly Regex ProfRx = Rx(@"проф|prof|подбер|підбер|best|рек");
    private static readonly Regex RamRx = Rx(@"озу|озп|ram|тайм|tim|памят|пам'ят");
    private static readonly Regex OptimiseRx = Rx(@"оптимиз|оптимізу|optimi[sz]e|настро|налашт|ускор|приско|boost|улучш|покращ");

    private bool CanTouch(TweakDef d) => !d.IsGuard && ProfileEngine.IsVisible(d, Hw) && IsSupported(d.Id);
    private string RecProfile => Advisor.RecommendedProfile(Info.Tier);

    // ---------------------------------------------------------------- overview card
    public string AiLine => AiLineFor(Loc.I.Lang);

    private string AiLineFor(string lang)
    {
        var reds = Advisor.RedParams(Scoring(), Platform, CanTouch);
        var head = Loc.I.Get("ai_rec", lang) + ": " + Loc.I.Get($"prof.{RecProfile}.name", lang) + ". ";
        return head + (reds.Count > 0
            ? Loc.I.Get("ai_red", lang) + " " + string.Join(", ", reds.Select(d => Loc.I.Get($"tw.{d.Id}.name", lang))) + "."
            : Loc.I.Get("ai_ok", lang));
    }

    [RelayCommand]
    private void AiPick() => SelectProfile(Profiles.First(x => x.Id == RecProfile));

    // ---------------------------------------------------------------- chat
    [RelayCommand]
    private void AiToggle()
    {
        AiOpen = !AiOpen;
        if (AiOpen && AiMessages.Count == 0) AiMessages.Add(new ChatMsg(Loc.I["ai_hi"], false));
    }

    [RelayCommand]
    private void AiClose() => AiOpen = false;

    [RelayCommand]
    private void AiSend()
    {
        var q = AiInput.Trim();
        if (q.Length == 0) return;
        AiInput = "";
        AiMessages.Add(new ChatMsg(q, true));
        ChatMsg answer;
        try { answer = Answer(q); }
        catch (Exception ex) { Corvite.Tweaks.Log.Info("ai failed: " + ex.Message); answer = new ChatMsg(Loc.I["ai_err"], false); }
        AiMessages.Add(answer);
    }

    private ChatMsg Answer(string q)
    {
        var x = q.ToLowerInvariant();
        var lang = QueryLang.Detect(x, Loc.I.Lang);
        string T(string k) => Loc.I.Get(k, lang);
        string Tail() => "\n\n" + T("ai.askgame");

        if (GreetRx.IsMatch(x)) return new ChatMsg(T("ai.greet") + Tail(), false);
        if (HelpRx.IsMatch(x) && FindTweak(x, lang) == null) return new ChatMsg(T("ai.help"), false);

        var tw = FindTweak(x, lang);
        var game = GameDb.Find(x);
        var asksExplain = ExplainRx.IsMatch(x);
        var asksWhere = WhereRx.IsMatch(x);

        if (tw != null && (asksExplain || asksWhere)) return TweakAnswer(tw, lang, asksWhere && !asksExplain) with { Text = TweakText(tw, lang, asksWhere && !asksExplain) + Tail() };
        if (game != null) return new ChatMsg(GameAnswer(game, lang), false);
        if (tw != null) return TweakAnswer(tw, lang, false) with { Text = TweakText(tw, lang, false) + Tail() };

        var sec = FindSection(x, lang);
        if (sec != null && asksWhere) return SectionAnswer(sec, lang) with { Text = SectionText(sec, lang) + Tail() };

        if (BackupRx.IsMatch(x)) return new ChatMsg(T("ai.n.backup") + Tail(), false);
        if (UndoRx.IsMatch(x)) return new ChatMsg(T("ai.n.undo") + Tail(), false);
        if (LockRx.IsMatch(x)) return new ChatMsg(T("ai.n.lock") + Tail(), false);
        if (PlatRx.IsMatch(x)) return new ChatMsg(T("ai.n.platform") + Tail(), false);
        if (SessRx.IsMatch(x)) return new ChatMsg(T("ai.n.session") + Tail(), false);
        if (ApplyRx.IsMatch(x)) return new ChatMsg(T("ai.n.apply") + Tail(), false);
        if (RiskRx.IsMatch(x)) return new ChatMsg(AiLineFor(lang) + "\n\n" + T("tip.sliderwarn") + Tail(), false);
        if (RamRx.IsMatch(x))
        {
            var ramt = Sections.First(s => s.Id == Section.RamTimings);
            return new ChatMsg(T("ai_ram") + Tail(), false, string.Format(T("ai.n.open"), Loc.I.Get($"n_{ramt.Key}", lang)), () => Navigate(Section.RamTimings));
        }
        if (ProfRx.IsMatch(x) || OptimiseRx.IsMatch(x))
            return new ChatMsg(T("ai_rec") + ": " + T($"prof.{RecProfile}.name") + "\n" + T("ai.n.profiles") + Tail(), false, T("ai_pick"), AiPick);
        return new ChatMsg(T("ai_def"), false);
    }

    // ---------------------------------------------------------------- finding a parameter / section by words in the question
    private static readonly HashSet<string> Stop = new() { "для", "при", "или", "the", "and", "with", "game", "игры", "игра", "игр", "для", "что", "как", "где", "это", "its" };
    private static readonly Dictionary<string, List<(string Key, HashSet<string> Stems)>> TweakIdx = new();
    private static readonly Dictionary<string, List<(string Key, HashSet<string> Stems)>> SecIdx = new();

    private static HashSet<string> Stems(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value)
             .Where(w => w.Length >= 3 && !Stop.Contains(w)).Select(w => w.Length > 5 ? w[..5] : w).ToHashSet();

    private static List<(string Key, HashSet<string> Stems)> IndexFor(string lang, bool sections)
    {
        var cache = sections ? SecIdx : TweakIdx;
        if (cache.TryGetValue(lang, out var ix)) return ix;
        ix = sections
            ? new[] { "overview", "hardware", "system", "cpu", "gpu", "memory", "ramt", "network", "background" }
                .Select(k => (k, Stems(Loc.I.Get($"n_{k}", lang)))).ToList()
            : TweakCatalog.All.Select(d => (d.Id, Stems(Loc.I.Get($"tw.{d.Id}.name", lang)))).ToList();
        cache[lang] = ix;
        return ix;
    }

    /// <summary>Best match by rare words of the name (a stem used by many names counts less). Null when nothing is convincing.</summary>
    private static string? Match(string x, string lang, bool sections)
    {
        var q = Stems(x);
        string? best = null; double bs = 0;
        foreach (var l in lang == "en" ? new[] { "en" } : new[] { lang, "en" })
        {
            var ix = IndexFor(l, sections);
            foreach (var (key, stems) in ix)
            {
                double s = 0;
                foreach (var st in stems)
                    if (q.Contains(st)) s += 1.0 / ix.Count(e => e.Stems.Contains(st));
                if (s > bs) { bs = s; best = key; }
            }
        }
        return bs >= 0.99 ? best : null;
    }

    private TweakDef? FindTweak(string x, string lang)
    {
        var id = Match(x, lang, false);
        return id == null ? null : TweakCatalog.All.FirstOrDefault(d => d.Id == id);
    }

    private SectionVm? FindSection(string x, string lang)
    {
        var k = Match(x, lang, true);
        return k == null ? null : Sections.FirstOrDefault(s => s.Key == k);
    }

    // ---------------------------------------------------------------- explain / where
    private SectionVm SectionOf(TweakDef d) => Sections.First(s => s.Id == d.Section);

    private ChatMsg TweakAnswer(TweakDef d, string lang, bool whereOnly)
    {
        var sec = SectionOf(d);
        return new ChatMsg("", false, string.Format(Loc.I.Get("ai.n.open", lang), Loc.I.Get($"n_{sec.Key}", lang)), () => Navigate(sec.Id));
    }

    private string TweakText(TweakDef d, string lang, bool whereOnly)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var sec = SectionOf(d);
        var sb = new StringBuilder();
        sb.Append(T($"tw.{d.Id}.name")).Append('\n');
        if (!whereOnly) sb.Append(T($"tw.{d.Id}.desc")).Append("\n\n");
        else sb.Append('\n');

        if (!whereOnly)
        {
            var fps = d.FpsMax == 0 ? T("tip.nofps")
                : d.IsToggle ? $"~+{d.FpsMin}–{d.FpsMax}%"
                : (lang == "en" ? "up to ~+" : "до ~+") + d.FpsMax + "% " + T("tip.atmax");
            sb.Append("• ").Append(T("ai.e.fps")).Append(": ").Append(fps).Append('\n');
            if (!d.IsGuard)
            {
                sb.Append("• ").Append(T("ai.e.temp")).Append(": ").Append(T($"tw.{d.Id}.temp")).Append('\n');
                sb.Append("• ").Append(T("ai.e.stab")).Append(": ").Append(T($"tw.{d.Id}.stab")).Append('\n');
                sb.Append("• ").Append(T("ai.e.lat")).Append(": ").Append(T($"tw.{d.Id}.lat")).Append('\n');
                sb.Append("• ").Append(T("ai.e.risk")).Append(": ").Append(d.IsToggle ? T($"risk.{d.Risk}") : T("ai.e.risk.s")).Append('\n');
            }
            if (d.HasNote) sb.Append('\n').Append(T($"tw.{d.Id}.note")).Append('\n');
            sb.Append('\n');
        }

        sb.Append(T("ai.e.where")).Append(": ").Append(string.Format(T("ai.e.where.fmt"), T($"n_{sec.Key}"), T(d.IsGuard ? "guard" : d.GroupKey))).Append('\n');
        if (!IsSupported(d.Id))
            sb.Append(T("ai.e.lock")).Append(" ").Append(T(LockKey(d.Id))).Append(". ").Append(T("ai.n.lock"));
        else
            sb.Append(T(d.IsGuard ? "ai.e.how.g" : d.IsToggle ? "ai.e.how.t" : "ai.e.how.s"));
        return sb.ToString().TrimEnd();
    }

    private ChatMsg SectionAnswer(SectionVm sec, string lang) =>
        new("", false, string.Format(Loc.I.Get("ai.n.open", lang), Loc.I.Get($"n_{sec.Key}", lang)), () => Navigate(sec.Id));

    private static string SectionText(SectionVm sec, string lang) =>
        string.Format(Loc.I.Get("ai.n.sec", lang), Loc.I.Get($"n_{sec.Key}", lang), Loc.I.Get($"d_{sec.Key}", lang));

    // ---------------------------------------------------------------- optimise for a game or genre
    private string GameAnswer(Game g, string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var plan = Advisor.PlanForGame(g, Info.Tier, Platform, CanTouch, id => _s[id]);
        foreach (var c in plan) _s[c.Def.Id] = c.To;
        if (plan.Count > 0) _profile = ProfileEngine.Custom;
        RefreshAll();

        var sb = new StringBuilder();
        sb.Append(g.Id).Append(": ").Append(T(g.Heavy ? "ai.g.why1" : "ai.g.why0")).Append(".\n");
        var gpu = Info.GpuName.Length > 0 ? Info.GpuName : "—";
        var cpu = Info.CpuName.Length > 0 ? Info.CpuName : "—";
        var ram = Info.Ram.Gb > 0 ? $"{Info.Ram.Gb:0.#} {T("gb")} {Info.Ram.Type}-{Info.Ram.SpeedMts}".Trim() : "—";
        sb.Append(T("ai.g.pc")).Append(": ").Append(cpu).Append(", ").Append(gpu).Append(", ").Append(ram).Append(".\n");
        if (!Info.GpuSupported) sb.Append(T("ai.g.arc")).Append('\n');
        if (Info.Tier < 2) sb.Append(T("ai.g.low")).Append('\n');
        if (Platform == PlatformKind.Laptop) sb.Append(T("ai.g.lap")).Append('\n');
        sb.Append('\n');

        if (plan.Count == 0) sb.Append(T("ai.g.none"));
        else
        {
            sb.Append(string.Format(T("ai.g.set"), plan.Count));
            foreach (var c in plan) AppendChange(sb, c, lang);
            sb.Append("\n\n").Append(string.Format(T("ai.g.total"), GainText, T($"tot.{ProfileEngine.RiskLevel(Risk)}")));
            sb.Append(' ').Append(T("ai.g.after"));
        }

        sb.Append("\n\n").Append(T("ai.g.ig")).Append('\n');
        sb.Append(g.InGame.TryGetValue(lang, out var ig) ? ig : g.InGame.GetValueOrDefault("en", ""));
        sb.Append("\n\n").Append(T("ai.g.hint"));
        return sb.ToString();
    }

    /// <summary>Text per changed parameter: what was set, the gain, what the user may raise by hand (never changed by the advisor).</summary>
    private void AppendChange(StringBuilder sb, AdviceChange c, string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var d = c.Def;
        sb.Append("\n• ").Append(T($"tw.{d.Id}.name")).Append(": ")
          .Append(d.IsToggle ? T("ai.g.onoff") : Fmt(d, c.From, lang) + " → " + Fmt(d, c.To, lang)).Append("\n   ");

        var (lo, hi) = AdvisorMath.GainOf(d, c.To, Platform);
        var gain = Math.Round(hi) < 1 ? T("ai.p.nofps")
            : Math.Round(lo) == Math.Round(hi) ? $"~+{Math.Round(hi)}% FPS" : $"~+{Math.Round(lo)}–{Math.Round(hi)}% FPS";

        if (d.IsToggle) { sb.Append(string.Format(T("ai.p.set.t"), gain)); return; }

        sb.Append(string.Format(T("ai.p.set.s"), Fmt(d, c.To, lang), gain));
        var green = AdvisorMath.TopOfZone(d, Platform, 0);
        var yellow = AdvisorMath.TopOfZone(d, Platform, 1);
        if (green > c.To + 1e-9)
            sb.Append(' ').Append(string.Format(T("ai.p.free"), Fmt(d, green, lang)));
        if (yellow > Math.Max(c.To, green) + 1e-9)
        {
            var temp = T($"tw.{d.Id}.temp");
            var cons = temp == T("ai.same") ? T("ai.p.cons.gen") : string.Format(T("ai.p.cons.temp"), temp);
            sb.Append(' ').Append(string.Format(T("ai.p.risk"), Fmt(d, yellow, lang), cons));
        }
    }

    private static string Fmt(TweakDef d, double value, string lang)
    {
        var v = (int)Math.Round(value);
        if (d.LabelCount > 0) return Loc.I.Get($"tw.{d.Id}.lbl{Math.Clamp(v, 0, d.LabelCount - 1)}", lang);
        var unit = d.UnitKey is null ? "" : Loc.I.Get(d.UnitKey, lang);
        var u = unit == "%" ? "%" : (unit.Length > 0 ? " " + unit : "");
        return (d.ShowPlus && v > 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture) + u;
    }
}
