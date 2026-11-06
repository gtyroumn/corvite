using System.Windows.Media;
using Corvite.App.Localization;
using Corvite.App.Theming;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corvite.App.ViewModels;

/// <summary>One row (toggle or slider). State lives in MainViewModel; this class only presents it.</summary>
public sealed class TweakVm : ObservableObject
{
    private readonly MainViewModel _o;
    public TweakDef Def { get; }

    public TweakVm(TweakDef def, MainViewModel owner) { Def = def; _o = owner; }

    public bool IsToggle => Def.IsToggle;
    public bool IsSlider => !Def.IsToggle;
    public bool IsGuard => Def.IsGuard;

    private string K(string f) => $"tw.{Def.Id}.{f}";
    public string Title => Loc.I[K("name")];
    public string Sub => Loc.I[K("sub")];
    public string Desc => Loc.I[K("desc")];

    public double Value
    {
        get => _o.GetValue(Def.Id);
        set { if (Math.Abs(value - _o.GetValue(Def.Id)) > 1e-9) _o.SetValue(Def.Id, value); }
    }

    public bool IsOn { get => Value >= 0.5; set => Value = value ? 1 : 0; }

    public double Minimum => Def.Min;
    public double Maximum => ProfileEngine.EffectiveMax(Def, _o.Platform);
    public double Step => Def.Step;

    public bool IsChanged => Math.Abs(_o.GetValue(Def.Id) - _o.GetApplied(Def.Id)) > 1e-9;

    /// <summary>Slider position 0..1 (drives the dimmed part of the track).</summary>
    public double Position => IsSlider ? ProfileEngine.PositionOf(Def, Value, _o.Platform) : 0;
    public int Zone => IsSlider ? ProfileEngine.ZoneOf(Position, _o.Platform) : Def.Risk;

    public Brush ZoneBrush => Palette.Risk(Zone);

    /// <summary>False for tweaks without a real implementation (or not applicable to this PC): the row is shown locked.</summary>
    public bool IsEnabled => _o.IsSupported(Def.Id);

    public string ChipText => !IsEnabled ? Loc.I[_o.LockKey(Def.Id)]
        : Def.IsGuard ? Loc.I["risk.guard"] : Loc.I[$"risk.{Def.Risk}"];
    public Brush ChipBrush => !IsEnabled ? Palette.Dim
        : Def.IsGuard ? Palette.AccentLight : Palette.Risk(Def.Risk);

    public string ValueText
    {
        get
        {
            if (!IsSlider) return "";
            var v = (int)Math.Round(Value);
            if (Def.LabelCount > 0) return Loc.I[K($"lbl{Math.Clamp(v, 0, Def.LabelCount - 1)}")];
            var unit = Def.UnitKey is null ? "" : Loc.I[Def.UnitKey];
            var u = unit == "%" ? "%" : " " + unit;
            return (Def.ShowPlus && v > 0 ? "+" : "") + v + u;
        }
    }

    public string LevelWord => !IsSlider || Def.IsGuard ? ""
        : Position <= 0 ? Loc.I["risk.std"] : Loc.I[$"risk.{Zone}"];

    public Brush LevelBrush => Position <= 0 ? Palette.Dim : Palette.Risk(Zone);

    // ---- tooltip (TZ section 6)
    public string TipFps
    {
        get
        {
            if (Def.FpsMax == 0) return Loc.I["tip.nofps"];
            if (Def.IsToggle) return $"~+{Def.FpsMin}–{Def.FpsMax}%";
            var pre = Loc.I.Lang switch { "en" => "up to ~+", _ => "до ~+" };
            return $"{pre}{Def.FpsMax}% {Loc.I["tip.atmax"]}";
        }
    }
    public string TipStab => Loc.I[K("stab")];
    public string TipLat => Loc.I[K("lat")];
    public string TipTemp => Loc.I[K("temp")];
    public bool TipShowToggleChip => IsToggle;
    public bool TipShowSliderWarn => IsSlider && !Def.IsGuard;
    public string TipSliderWarn => Loc.I["tip.sliderwarn"];
    public bool HasNote => Def.HasNote;
    public string Note => Def.HasNote ? Loc.I[K("note")] : "";
    public string TipFoot => Loc.I["tip.foot"];

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class GroupVm : ObservableObject
{
    private readonly string _key;
    public GroupVm(string key, List<TweakVm> items) { _key = key; Items = items; }
    public string Title => Loc.I[_key];
    public List<TweakVm> Items { get; }
    public void Refresh() => OnPropertyChanged(string.Empty);
}
