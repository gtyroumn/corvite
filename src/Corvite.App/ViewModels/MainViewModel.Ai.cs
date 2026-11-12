using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Corvite.App.Localization;
using Corvite.Core.Ai;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

public sealed record ChatMsg(string Text, bool IsUser);

/// <summary>
/// Stage 7: AI advisor on rules (no model). Overview card + floating chat. Everything it changes stays a pending change
/// until the user presses "Apply" (TZ section 10); the validator lives in Corvite.Core.Ai.Advisor.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<ChatMsg> AiMessages { get; } = new();
    [ObservableProperty] private bool _aiOpen;
    [ObservableProperty] private string _aiInput = "";

    private static readonly Regex RiskRx = new("риск|ризик|risk|опас|небезп|danger", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProfRx = new("проф|prof|подбер|підбер|best|рек", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RamRx = new("озу|озп|ram|тайм|tim|памят|пам'ят", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
    private void AiPick()
    {
        var p = Profiles.First(x => x.Id == RecProfile);
        SelectProfile(p);
    }

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
        string answer;
        try { answer = Answer(q); }
        catch (Exception ex) { Corvite.Tweaks.Log.Info("ai failed: " + ex.Message); answer = Loc.I["ai_err"]; }
        AiMessages.Add(new ChatMsg(answer, false));
    }

    private string Answer(string q)
    {
        var x = q.ToLowerInvariant();
        var lang = QueryLang.Detect(x, Loc.I.Lang);
        var game = GameDb.Find(x);
        if (game != null) return GameAnswer(game, lang);
        if (RiskRx.IsMatch(x)) return AiLineFor(lang);
        if (ProfRx.IsMatch(x)) return Loc.I.Get("ai_rec", lang) + ": " + Loc.I.Get($"prof.{RecProfile}.name", lang);
        if (RamRx.IsMatch(x)) return Loc.I.Get("ai_ram", lang);
        return Loc.I.Get("ai_def", lang);
    }

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
            sb.Append(T("ai.g.set")).Append(plan.Count).Append(T("ai.g.after"));
            foreach (var c in plan)
            {
                var change = c.Def.IsToggle ? T("ai.g.onoff") : Fmt(c.Def, c.From, lang) + " → " + Fmt(c.Def, c.To, lang);
                sb.Append("\n• ").Append(T($"tw.{c.Def.Id}.name")).Append(": ").Append(change)
                  .Append('\n').Append("   ").Append(T($"tw.{c.Def.Id}.sub"));
            }
        }

        sb.Append("\n\n").Append(T("ai.g.ig")).Append('\n');
        sb.Append(g.InGame.TryGetValue(lang, out var ig) ? ig : g.InGame.GetValueOrDefault("en", ""));
        sb.Append("\n\n").Append(T("ai.g.hint"));
        return sb.ToString();
    }

    private static string Fmt(TweakDef d, double value, string lang)
    {
        var v = (int)Math.Round(value);
        if (d.LabelCount > 0) return Loc.I.Get($"tw.{d.Id}.lbl{Math.Clamp(v, 0, d.LabelCount - 1)}", lang);
        var unit = d.UnitKey is null ? "" : Loc.I.Get(d.UnitKey, lang);
        var u = unit == "%" ? "%" : (unit.Length > 0 ? " " + unit : "");
        return (d.ShowPlus && v > 0 ? "+" : "") + v + u;
    }
}
