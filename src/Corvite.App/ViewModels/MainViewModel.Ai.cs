using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Corvite.App.Localization;
using Corvite.Core.Ai;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

/// <summary>
/// Corvi, the AI advisor (stage 7, rules-based; a local model can be added later behind the same entry point).
/// What it does: optimise for a game or genre (a checklist of changes; they stay pending until "Apply"), explain any parameter,
/// show where things are in the app, assess risk, find the reason of lags, run simple commands, remember the topic of the dialogue.
/// Validation lives in Corvite.Core.Ai.Advisor. Answers are short; details and next steps come as buttons under the message.
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
    private static readonly Regex WhereRx = Rx(@"где|куда|как (найти|открыть|включить|выключить|перейти|посмотреть|изменить|применить)|where|how (do i|can i|to) |де |куди|як (знайти|відкрити|увімкнути|вимкнути|перейти|подивитись|змінити|застосувати)");
    private static readonly Regex BackupRx = Rx(@"резерв|бэкап|бекап|backup|копи[яию]|исходн|вихідн|original|restore point|точк|відновлен|восстанов");
    private static readonly Regex RevertRx = Rx(@"сброс|скин[иь]|discard|скинут|отмени(ть)? (не|измен|подготов)|скасуй (зміни|підготов)");
    private static readonly Regex UndoRx = Rx(@"откат|отмен|верн[уи]|undo|revert|скасув|поверн|відкат");
    private static readonly Regex ApplyRx = Rx(@"примен|apply|застос");
    private static readonly Regex PlatRx = Rx(@"ноутбук|laptop|платформ|platform|пк или");
    private static readonly Regex LockRx = Rx(@"заблок|серый|недоступн|locked|unavailable|нужен драйвер|потрібен драйвер|скоро");
    private static readonly Regex SessRx = Rx(@"сесси|session|автоматическ|automatic|автоматичн|во время игры|during the game|під час гри");
    private static readonly Regex RiskRx = Rx(@"риск|ризик|risk|опас|небезп|danger|безопасн|safe");
    private static readonly Regex ProfRx = Rx(@"проф|prof|подбер|підбер|best|рек");
    private static readonly Regex RamRx = Rx(@"озу|озп|ram|тайм|tim|памят|пам'ят");
    private static readonly Regex OptimiseRx = Rx(@"оптимиз|оптимізу|optimi[sz]e|настро|налашт|ускор|приско|boost|улучш|покращ");
    private static readonly Regex LagRx = Rx(@"лаг|тормоз|фриз|заикан|подлаг|просад|низк\w* fps|мало fps|fps (упал|низк|мал)|lag|stutter|freez|slow|low fps|fps drop|гальм|фріз|затина|просід|нізьк\w* fps");
    private static readonly Regex ShowRx = Rx(@"что я (менял|изменил|поменял)|покажи.{0,20}измен|список измен|what (did|have) i change|show.{0,20}changes|що я змін|покажи.{0,20}змін|список змін");
    private static readonly Regex OpenRx = Rx(@"^\s*(пожалуйста |please |будь ласка )?(открой|откройте|перейди|перейти|покажи раздел|open|go to|відкрий|перейди)\b");
    private static readonly Regex AddGameRx = Rx(@"^\s*(добав\w*|запомни|add|remember|додай\w*)\s+(?:(?:мою|свою|новую|нову|my|new|a|the)\s+)?(?:игру|игра|game|гру|гра)\s+(.+)$");
    private static readonly Regex DetailRx = Rx(@"подробн|детал|details?|more|докладн|почему|зачем|why|чому|навіщо|поясни|объясни|explain|выше|больше|поднять|raise|higher|вище|більше");
    private static readonly Regex InGameRx = Rx(@"в игре|настройки игры|in-?game|in game|график|graphics|в грі|налаштування гри");
    private static readonly Regex CancelRx = Rx(@"^\s*(отмена|отмени|не надо|нет|cancel|no|скасуй|ні)\s*$");

    private bool CanTouch(TweakDef d) => !d.IsGuard && ProfileEngine.IsVisible(d, Hw) && IsSupported(d.Id);
    private string RecProfile => Advisor.RecommendedProfile(Info.Tier);

    // ---------------------------------------------------------------- what the dialogue is about (for "why?", "more", "where is it?")
    private enum AiTopic { None, Tweak, Game }
    private AiTopic _topic = AiTopic.None;
    private TweakDef? _topicTweak;
    private Game? _topicGame;
    private List<AdviceChange> _topicPlan = new();
    private string? _pendingName;   // a game the player asked to add; waiting for the genre

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
        if (AiOpen && AiMessages.Count == 0) AiMessages.Add(GreetMsg());
    }

    /// <summary>Opens the chat on a question of Corvi (used by the hints on the Overview).</summary>
    private void AiOpenWith(Func<ChatMsg> msg)
    {
        AiOpen = true;
        if (AiMessages.Count == 0) AiMessages.Add(GreetMsg());
        AiMessages.Add(msg());
    }

    private ChatMsg GreetMsg()
    {
        var l = Loc.I.Lang;
        return new ChatMsg(Loc.I["ai_hi"], false, Greeting: true, Chips: new[]
        {
            GameChip(l, true), LagChip(l), HelpChip(l)
        });
    }

    private ChatBtn GameChip(string lang, bool primary = false) { var t = Loc.I.Get("ai.c.game", lang); return new ChatBtn(t, () => Ask(t, () => AskGameMsg(lang)), primary); }
    private ChatBtn LagChip(string lang) { var t = Loc.I.Get("ai.c.lag", lang); return new ChatBtn(t, () => Ask(t, () => DiagMsg(lang))); }
    private ChatBtn HelpChip(string lang) { var t = Loc.I.Get("ai.c.help", lang); return new ChatBtn(t, () => Ask(t, () => new ChatMsg(Loc.I.Get("ai.help", lang), false, Chips: new[] { GameChip(lang, true), LagChip(lang) }))); }

    /// <summary>The opening message follows the interface language; answers keep the language of the question.</summary>
    private void AiRelocalize()
    {
        if (AiMessages.Count > 0 && AiMessages[0].Greeting) AiMessages[0] = GreetMsg();
    }

    [RelayCommand]
    private void AiClose() => AiOpen = false;

    [RelayCommand]
    private void AiSend()
    {
        var q = AiInput.Trim();
        if (q.Length == 0) return;
        AiInput = "";
        Ask(q, () => Answer(q));
    }

    /// <summary>Shows the player's line (a typed question or the label of a pressed button) and Corvi's answer.</summary>
    private void Ask(string userText, Func<ChatMsg> answer)
    {
        AiMessages.Add(new ChatMsg(userText, true));
        ChatMsg a;
        try { a = answer(); }
        catch (Exception ex) { Corvite.Tweaks.Log.Info("ai failed: " + ex.Message); a = new ChatMsg(Loc.I["ai_err"], false); }
        AiMessages.Add(a);
    }

    private void Say(ChatMsg m) => AiMessages.Add(m);

    /// <summary>An answer that ends with the question about optimising for a game, as a short line plus a button.</summary>
    private ChatMsg Tailed(string text, string lang, params ChatBtn[] chips)
    {
        var list = chips.ToList();
        list.Add(GameChip(lang));
        return new ChatMsg(text + "\n\n" + Loc.I.Get("ai.askgame", lang), false, Chips: list);
    }

    private ChatBtn NavChip(Section id, string lang, bool primary = false)
    {
        var sec = Sections.First(x => x.Id == id);
        return new ChatBtn(string.Format(Loc.I.Get("ai.n.open", lang), Loc.I.Get($"n_{sec.Key}", lang)), () => Navigate(id), primary);
    }

    private ChatMsg Answer(string q)
    {
        var x = q.ToLowerInvariant();
        var lang = QueryLang.Detect(x, Loc.I.Lang);
        string T(string k) => Loc.I.Get(k, lang);

        // a game was announced ("add game X"): the next line is its genre
        if (_pendingName != null)
        {
            var name = _pendingName;
            if (CancelRx.IsMatch(x)) { _pendingName = null; return new ChatMsg(T("ai.u.cancel"), false); }
            var gen = GenreOf(x);
            if (gen != null) return AddGame(name, gen, lang);
            _pendingName = null;
        }

        if (GreetRx.IsMatch(x)) return Tailed(T("ai.greet"), lang);

        var tw = FindTweak(x, lang);
        if (HelpRx.IsMatch(x) && tw == null) return new ChatMsg(T("ai.help"), false, Chips: new[] { GameChip(lang, true), LagChip(lang) });

        // commands
        var add = AddGameRx.Match(x);
        if (add.Success) return AskGenre(q.Substring(add.Groups[2].Index).Trim().Trim('«', '»', '"', '\'', '“', '”', '.', '!'), lang);
        if (ShowRx.IsMatch(x)) return ChangesMsg(lang);
        if (OpenRx.IsMatch(x))
        {
            var target = FindSection(x, lang);
            if (target != null) { Navigate(target.Id); return new ChatMsg(string.Format(T("ai.o.done"), Loc.I.Get($"n_{target.Key}", lang)), false); }
        }

        var game = GameDb.Find(x);
        var asksExplain = ExplainRx.IsMatch(x);
        var asksWhere = WhereRx.IsMatch(x);

        if (tw != null && (asksExplain || asksWhere)) return TweakMsg(tw, lang, asksWhere && !asksExplain ? TweakMode.Where : TweakMode.Short);
        if (game != null) return GameMsg(game, lang);
        if (tw != null) return TweakMsg(tw, lang, TweakMode.Short);

        // short follow-up about the current topic ("why?", "more", "where is it?")
        if (x.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6)
        {
            var f = FollowUp(x, lang, asksWhere);
            if (f != null) return f;
        }

        var sec = FindSection(x, lang);
        if (sec != null && asksWhere) return Tailed(SectionText(sec, lang), lang, NavChip(sec.Id, lang, true));

        if (LagRx.IsMatch(x)) return DiagMsg(lang);
        if (RevertRx.IsMatch(x)) return RevertMsg(lang);
        if (BackupRx.IsMatch(x)) return Tailed(T("ai.n.backup"), lang, NavChip(Section.Overview, lang, true));
        if (UndoRx.IsMatch(x)) return UndoMsg(lang);
        if (LockRx.IsMatch(x)) return Tailed(T("ai.n.lock"), lang);
        if (PlatRx.IsMatch(x)) return Tailed(T("ai.n.platform"), lang);
        if (SessRx.IsMatch(x)) return Tailed(T("ai.n.session"), lang);
        if (ApplyRx.IsMatch(x)) return ApplyMsg(lang, asksWhere || asksExplain);
        if (RiskRx.IsMatch(x)) return Tailed(AiLineFor(lang) + "\n\n" + T("tip.sliderwarn"), lang);
        if (RamRx.IsMatch(x)) return Tailed(T("ai_ram"), lang, NavChip(Section.RamTimings, lang, true));
        if (ProfRx.IsMatch(x) || OptimiseRx.IsMatch(x))
            return Tailed(T("ai_rec") + ": " + T($"prof.{RecProfile}.name") + "\n" + T("ai.n.profiles"), lang, new ChatBtn(T("ai_pick"), AiPick, true));

        // typos: offer, never act on a guess
        var fg = GameDb.FindFuzzy(x);
        if (fg != null)
            return new ChatMsg(string.Format(T("ai.z.game"), fg.Id), false, Chips: new[] { new ChatBtn(fg.Id, () => Ask(fg.Id, () => GameMsg(fg, lang)), true) });
        var ft = FindTweak(x, lang, fuzzy: true);
        if (ft != null)
            return new ChatMsg(string.Format(T("ai.z.tweak"), T($"tw.{ft.Id}.name")), false, Chips: new[]
            {
                new ChatBtn(T("ai.c.explain"), () => Ask(T("ai.c.explain"), () => TweakMsg(ft, lang, TweakMode.Short)), true)
            });

        var sug = Suggest(x, lang);
        if (sug.Count > 0)
            return new ChatMsg(T("ai.z.many"), false, Chips: sug.Select(d => new ChatBtn(T($"tw.{d.Id}.name"),
                () => Ask(T($"tw.{d.Id}.name"), () => TweakMsg(d, lang, TweakMode.Short)))).ToList());

        return new ChatMsg(T("ai_def"), false, Chips: new[] { GameChip(lang, true), LagChip(lang), HelpChip(lang) });
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

    /// <summary>Score of every name by rare words of the question (a stem used by many names counts less). With fuzzy on, a misspelt word is replaced by the closest known stem.</summary>
    private static List<(string Key, double Score)> Scores(string x, string lang, bool sections, bool fuzzy)
    {
        var q = Stems(x);
        var res = new Dictionary<string, double>();
        foreach (var l in lang == "en" ? new[] { "en" } : new[] { lang, "en" })
        {
            var ix = IndexFor(l, sections);
            var qq = q;
            if (fuzzy)
            {
                var vocab = ix.SelectMany(e => e.Stems).ToHashSet();
                qq = new HashSet<string>(q);
                foreach (var w in q.Where(w => w.Length >= 5 && !vocab.Contains(w)))
                {
                    var c = Fuzzy.Closest(w, vocab, Fuzzy.Allowed(w.Length));
                    if (c != null) qq.Add(c);
                }
            }
            foreach (var (key, stems) in ix)
            {
                double s = 0;
                foreach (var st in stems)
                    if (qq.Contains(st)) s += 1.0 / ix.Count(e => e.Stems.Contains(st));
                if (s > 0 && (!res.TryGetValue(key, out var old) || s > old)) res[key] = s;
            }
        }
        return res.Select(kv => (Key: kv.Key, Score: kv.Value)).OrderByDescending(t => t.Score).ToList();
    }

    private static string? Match(string x, string lang, bool sections, bool fuzzy = false)
    {
        var top = Scores(x, lang, sections, fuzzy);
        return top.Count > 0 && top[0].Score >= 0.99 ? top[0].Key : null;
    }

    private TweakDef? FindTweak(string x, string lang, bool fuzzy = false)
    {
        var id = Match(x, lang, false, fuzzy);
        return id == null ? null : TweakCatalog.All.FirstOrDefault(d => d.Id == id);
    }

    private SectionVm? FindSection(string x, string lang)
    {
        var k = Match(x, lang, true);
        return k == null ? null : Sections.FirstOrDefault(s => s.Key == k);
    }

    /// <summary>Up to three parameters that partly match the question: "did you mean ...".</summary>
    private List<TweakDef> Suggest(string x, string lang) =>
        Scores(x, lang, false, true).Where(t => t.Score >= 0.34).Take(3)
            .Select(t => TweakCatalog.All.FirstOrDefault(d => d.Id == t.Key)).Where(d => d != null).Select(d => d!).ToList();

    // ---------------------------------------------------------------- explain / where
    private SectionVm SectionOf(TweakDef d) => Sections.First(s => s.Id == d.Section);

    private enum TweakMode { Short, Full, Where }

    private ChatMsg TweakMsg(TweakDef d, string lang, TweakMode mode)
    {
        _topic = AiTopic.Tweak; _topicTweak = d;
        var sec = SectionOf(d);
        var chips = new List<ChatBtn> { NavChip(sec.Id, lang, true) };
        if (mode == TweakMode.Short)
            chips.Add(new ChatBtn(Loc.I.Get("ai.c.more", lang), () => Say(TweakMsg(d, lang, TweakMode.Full))));
        return Tailed(TweakText(d, lang, mode), lang, chips.ToArray());
    }

    private string TweakText(TweakDef d, string lang, TweakMode mode)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var sec = SectionOf(d);
        var sb = new StringBuilder();
        sb.Append(T($"tw.{d.Id}.name")).Append('\n');
        if (mode != TweakMode.Where) sb.Append(T($"tw.{d.Id}.desc")).Append("\n\n");
        else sb.Append('\n');

        if (mode != TweakMode.Where)
        {
            var fps = d.FpsMax == 0 ? T("tip.nofps")
                : d.IsToggle ? $"~+{d.FpsMin}–{d.FpsMax}%"
                : (lang == "en" ? "up to ~+" : "до ~+") + d.FpsMax + "% " + T("tip.atmax");
            sb.Append("• ").Append(T("ai.e.fps")).Append(": ").Append(fps).Append('\n');
            if (!d.IsGuard)
            {
                if (mode == TweakMode.Full)
                {
                    sb.Append("• ").Append(T("ai.e.temp")).Append(": ").Append(T($"tw.{d.Id}.temp")).Append('\n');
                    sb.Append("• ").Append(T("ai.e.stab")).Append(": ").Append(T($"tw.{d.Id}.stab")).Append('\n');
                    sb.Append("• ").Append(T("ai.e.lat")).Append(": ").Append(T($"tw.{d.Id}.lat")).Append('\n');
                }
                sb.Append("• ").Append(T("ai.e.risk")).Append(": ").Append(d.IsToggle ? T($"risk.{d.Risk}") : T("ai.e.risk.s")).Append('\n');
            }
            if (mode == TweakMode.Full && d.HasNote) sb.Append('\n').Append(T($"tw.{d.Id}.note")).Append('\n');
            sb.Append('\n');
        }

        if (mode != TweakMode.Short || !IsSupported(d.Id))
            sb.Append(T("ai.e.where")).Append(": ").Append(string.Format(T("ai.e.where.fmt"), T($"n_{sec.Key}"), T(d.IsGuard ? "guard" : d.GroupKey))).Append('\n');
        if (!IsSupported(d.Id))
            sb.Append(T("ai.e.lock")).Append(" ").Append(T(LockKey(d.Id))).Append(". ").Append(T("ai.n.lock"));
        else if (mode != TweakMode.Short)
            sb.Append(T(d.IsGuard ? "ai.e.how.g" : d.IsToggle ? "ai.e.how.t" : "ai.e.how.s"));
        return sb.ToString().TrimEnd();
    }

    private static string SectionText(SectionVm sec, string lang) =>
        string.Format(Loc.I.Get("ai.n.sec", lang), Loc.I.Get($"n_{sec.Key}", lang), Loc.I.Get($"d_{sec.Key}", lang));

    /// <summary>Answer to "why", "more", "where is it" when the question itself names nothing new.</summary>
    private ChatMsg? FollowUp(string x, string lang, bool asksWhere)
    {
        if (_topic == AiTopic.Tweak && _topicTweak != null)
        {
            if (asksWhere) return TweakMsg(_topicTweak, lang, TweakMode.Where);
            if (DetailRx.IsMatch(x) || RiskRx.IsMatch(x)) return TweakMsg(_topicTweak, lang, TweakMode.Full);
        }
        if (_topic == AiTopic.Game && _topicGame != null)
        {
            if (InGameRx.IsMatch(x)) return InGameMsg(_topicGame, lang);
            if (DetailRx.IsMatch(x) || RiskRx.IsMatch(x)) return DetailsMsg(lang);
        }
        return null;
    }

    // ---------------------------------------------------------------- optimise for a game or genre
    private ChatMsg AskGameMsg(string lang)
    {
        string[] pop = { "CS2", "Valorant", "Fortnite", "Apex Legends", "Dota 2" };
        var chips = new List<ChatBtn>();
        foreach (var id in pop.Concat(new[] { "FPS", "MOBA", "TPS" }))
        {
            var g = GameDb.All.FirstOrDefault(e => e.Id == id);
            if (g != null) chips.Add(new ChatBtn(id, () => Ask(id, () => GameMsg(g, lang))));
        }
        return new ChatMsg(Loc.I.Get("ai.g.ask", lang), false, Chips: chips);
    }

    private ChatMsg GameMsg(Game g, string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var plan = Advisor.PlanForGame(g, Info.Tier, Platform, CanTouch, id => _s[id]);
        _topic = AiTopic.Game; _topicGame = g; _topicPlan = plan;

        var sb = new StringBuilder();
        sb.Append(g.Id).Append(": ").Append(T(g.Heavy ? "ai.g.why1" : "ai.g.why0")).Append(".\n");
        var gpu = Info.GpuName.Length > 0 ? Info.GpuName : "—";
        var cpu = Info.CpuName.Length > 0 ? Info.CpuName : "—";
        var ram = Info.Ram.Gb > 0 ? $"{Info.Ram.Gb:0.#} {T("gb")} {Info.Ram.Type}-{Info.Ram.SpeedMts}".Trim() : "—";
        sb.Append(T("ai.g.pc")).Append(": ").Append(cpu).Append(", ").Append(gpu).Append(", ").Append(ram).Append('.');
        if (!Info.GpuSupported) sb.Append('\n').Append(T("ai.g.arc"));
        if (Info.Tier < 2) sb.Append('\n').Append(T("ai.g.low"));
        if (Platform == PlatformKind.Laptop) sb.Append('\n').Append(T("ai.g.lap"));

        var chips = new List<ChatBtn>();
        ChangeSet? set = null;
        if (plan.Count == 0) sb.Append("\n\n").Append(T("ai.g.none"));
        else
        {
            sb.Append("\n\n").Append(string.Format(T("ai.g.set"), plan.Count));
            var cs = new ChangeSet(plan.Select(c => new ChangeItemVm(c, ChangeTitle(c, lang), GainShort(c.Def, c.To, lang))).ToList());
            set = cs;
            chips.Add(new ChatBtn(T("ai.s.btn"), () => Say(StageMsg(cs, lang)), true));
            chips.Add(new ChatBtn(T("ai.c.params"), () => Say(DetailsMsg(lang))));
        }
        chips.Add(new ChatBtn(T("ai.c.ingame"), () => Say(InGameMsg(g, lang))));
        return new ChatMsg(sb.ToString(), false, Chips: chips, Changes: set);
    }

    private string ChangeTitle(AdviceChange c, string lang)
    {
        var name = Loc.I.Get($"tw.{c.Def.Id}.name", lang);
        return c.Def.IsToggle ? name + ": " + Loc.I.Get("on", lang) : name + ": " + Fmt(c.Def, c.From, lang) + " → " + Fmt(c.Def, c.To, lang);
    }

    private string GainShort(TweakDef d, double to, string lang)
    {
        var (lo, hi) = AdvisorMath.GainOf(d, to, Platform);
        if (Math.Round(hi) < 1) return Loc.I.Get("ai.p.nofps.s", lang);
        return Math.Round(lo) == Math.Round(hi) ? $"~+{Math.Round(hi)}% FPS" : $"~+{Math.Round(lo)}–{Math.Round(hi)}% FPS";
    }

    /// <summary>The ticked changes go to the pending list (never lowering a value the player set higher); nothing is applied to the system yet.</summary>
    private ChatMsg StageMsg(ChangeSet set, string lang)
    {
        string T(string k) => Loc.I.Get(k, lang);
        var sel = set.Items.Where(i => i.IsChecked).ToList();
        if (sel.Count == 0) return new ChatMsg(T("ai.s.none"), false);
        foreach (var i in sel)
        {
            var id = i.Change.Def.Id;
            if (CanTouch(i.Change.Def)) _s[id] = Math.Max(_s[id], i.Change.To);
        }
        _profile = ProfileEngine.Custom;
        RefreshAll();
        var text = string.Format(T("ai.s.done"), sel.Count) + " " + string.Format(T("ai.g.total"), GainText, T($"tot.{ProfileEngine.RiskLevel(Risk)}"));
        return new ChatMsg(text, false, Chips: new[]
        {
            new ChatBtn(T("ai.a.btn"), () => ApplyCommand.Execute(null), true),
            new ChatBtn(T("ai.r.btn"), () => { RevertCommand.Execute(null); Say(new ChatMsg(T("ai.r.done"), false)); })
        });
    }

    /// <summary>Per parameter: what was set, the gain, what the player may raise by hand (never changed by the advisor).</summary>
    private ChatMsg DetailsMsg(string lang)
    {
        var sb = new StringBuilder();
        foreach (var c in _topicPlan) AppendChange(sb, c, lang);
        if (sb.Length == 0) return new ChatMsg(Loc.I.Get("ai.g.none", lang), false);
        return new ChatMsg(sb.ToString().TrimStart('\n'), false);
    }

    private ChatMsg InGameMsg(Game g, string lang)
    {
        var ig = g.InGame.TryGetValue(lang, out var t) ? t : g.InGame.GetValueOrDefault("en", "");
        return new ChatMsg(Loc.I.Get("ai.g.ig", lang) + "\n" + ig + "\n\n" + Loc.I.Get("ai.g.hint", lang), false);
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
