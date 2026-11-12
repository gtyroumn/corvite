using System.Windows.Input;
using System.Windows.Media;
using Corvite.App.Localization;
using Corvite.App.Theming;
using Corvite.Core.Catalog;
using Corvite.Core.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Corvite.App.ViewModels;

public sealed class SectionVm : ObservableObject
{
    public Section Id { get; }
    public string IconData { get; }
    private readonly string _key;
    private int _count;
    private bool _selected;

    public SectionVm(Section id, string key, string icon) { Id = id; _key = key; IconData = icon; }

    public string Name => Loc.I[$"n_{_key}"];
    public string Description => Loc.I[$"d_{_key}"];
    public string CountText => _count > 0 ? _count.ToString() : "";
    public bool HasCounter => Id is not (Section.Overview or Section.Hardware or Section.RamTimings);

    public int Count { set { _count = value; OnPropertyChanged(nameof(CountText)); } }
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Description)); }
}

public sealed class ProfileVm : ObservableObject
{
    public string Id { get; }
    public int Level { get; }
    private bool _selected;

    public ProfileVm(string id, int level) { Id = id; Level = level; }
    public string Name => Loc.I[$"prof.{Id}.name"];
    public string Sub => Loc.I[$"prof.{Id}.sub"];
    public bool Dot1 => Level >= 1;
    public bool Dot2 => Level >= 2;
    public bool Dot3 => Level >= 3;
    public bool Dot4 => Level >= 4;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Sub)); }
}

public sealed class MonitorCardVm : ObservableObject
{
    private readonly string _titleKey;
    private readonly string _unit;
    private readonly bool _unitIsKey;
    private readonly List<double> _hist = new();
    private double _lo, _hi;

    public string Value { get; private set; } = "—";
    public string SubText { get; private set; } = "";
    public bool IsDemo { get; private set; } = true;
    public PointCollection Points { get; private set; } = new();
    public PointCollection Area { get; private set; } = new();

    public MonitorCardVm(string titleKey, string unit, double lo, double hi, bool unitIsKey = false)
    { _titleKey = titleKey; _unit = unit; _unitIsKey = unitIsKey; _lo = lo; _hi = hi; }

    public string Unit => _unitIsKey ? Loc.I[_unit] : _unit;
    public string Title => Loc.I[_titleKey];
    public string DemoTag => Loc.I["ov.demo"];
    public void Refresh() => OnPropertyChanged(string.Empty);

    public void SetRange(double lo, double hi) { _lo = lo; _hi = hi > lo ? hi : lo + 1; }

    /// <summary>v == null: the sensor is unavailable, the history is left untouched and the value shows a dash.</summary>
    public void Push(double? v, string sub, string valueText, bool demo)
    {
        IsDemo = demo;
        Value = valueText; SubText = sub;
        if (v is double d)
        {
            _hist.Add(d);
            if (_hist.Count > 40) _hist.RemoveAt(0);
            var pts = new PointCollection();
            for (int i = 0; i < _hist.Count; i++)
            {
                var x = _hist.Count > 1 ? i / (double)(_hist.Count - 1) * 100 : 0;
                var y = 28 - Math.Clamp((_hist[i] - _lo) / (_hi - _lo), 0, 1) * 26;
                pts.Add(new System.Windows.Point(x, y));
            }
            var area = new PointCollection { new System.Windows.Point(0, 30) };
            foreach (var pt in pts) area.Add(pt);
            area.Add(new System.Windows.Point(100, 30));
            Points = pts; Area = area;
        }
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Base class for content pages shown in the main area.</summary>
public abstract class PageVm : ObservableObject
{
    public virtual void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class OverviewPage : PageVm
{
    public MainViewModel Main { get; }
    public OverviewPage(MainViewModel main) { Main = main; }
    public string Title => Loc.I["n_overview"];
    public string Intro => Loc.I["ov.intro"];
}

public sealed class SectionPage : PageVm
{
    private readonly SectionVm _sec;
    public List<GroupVm> Groups { get; }
    public ICommand ResetCommand { get; }
    public SectionPage(SectionVm sec, List<GroupVm> groups, ICommand reset) { _sec = sec; Groups = groups; ResetCommand = reset; }
    public string Title => _sec.Name;
    public string Description => _sec.Description;
    public override void Refresh() { base.Refresh(); foreach (var g in Groups) { g.Refresh(); foreach (var t in g.Items) t.Refresh(); } }
}

/// <summary>Placeholder for pages that belong to later development stages (TZ section 15).</summary>
public sealed class PlannedPage : PageVm
{
    private readonly SectionVm _sec;
    private readonly string _stage;
    public PlannedPage(SectionVm sec, string stage) { _sec = sec; _stage = stage; }
    public string Title => _sec.Name;
    public string Description => _sec.Description;
    public string Stage => _stage;
    public string Text => Loc.I[_stage == "unsup" ? "unsup_gpu" : "planned"];
}

// ---------------------------------------------------------------- Hardware & clocks page (stage 2)

public sealed class HwRowVm : ObservableObject
{
    private readonly string _key;
    private string _value;
    public HwRowVm(string key, string value) { _key = key; _value = value; }
    public string Label => Loc.I[_key];
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

public sealed class HwCardVm
{
    public string Title { get; init; } = "";
    public List<HwRowVm> Rows { get; init; } = new();
}

public sealed class DemoItemVm
{
    public Corvite.Core.Hardware.HardwareInfo Info { get; init; } = null!;
    public string Cpu { get; init; } = "";
    public string Gpu { get; init; } = "";
    public bool IsSelected { get; init; }
}

/// <summary>"Hardware &amp; clocks": detected CPU / GPU / RAM / board with live clocks (TZ 4.2).</summary>
public sealed class HardwarePage : PageVm
{
    private const string Dash = "—";
    private readonly MainViewModel _m;
    private HwRowVm? _cpuClk, _cpuPw, _gpuClk, _gpuMem, _vram;

    public List<HwCardVm> Cards { get; private set; } = new();
    public List<DemoItemVm> Demos { get; private set; } = new();

    public HardwarePage(MainViewModel m) { _m = m; Build(); }

    public string Title => Loc.I["n_hardware"];
    public string Description => Loc.I["d_hardware"];
    public bool ShowDemo => _m.DemoEnabled;
    public string SourceNote => _m.HwLoading ? Loc.I["hw_loading"] : _m.Info.IsDemo ? Loc.I["h_cfg"] : Loc.I["h_real"];

    public string Caption
    {
        get
        {
            var i = _m.Info;
            var g = i.GpuBrand switch
            {
                Corvite.Core.Catalog.GpuVendor.Nvidia => "NVIDIA",
                Corvite.Core.Catalog.GpuVendor.Amd => "AMD",
                Corvite.Core.Catalog.GpuVendor.IntelArc => "Intel",
                _ => "?"
            };
            var c = i.CpuBrand == Corvite.Core.Catalog.CpuVendor.Amd ? "AMD" : "Intel";
            return $"{Loc.I["vp"]}: {c} + {g}";
        }
    }

    public bool ShowUnsupported => !_m.HwLoading && !_m.Info.GpuSupported;
    public string UnsupportedText => _m.Info.GpuBrand == Corvite.Core.Catalog.GpuVendor.IntelArc ? Loc.I["unsup"] : Loc.I["nosup"];

    public override void Refresh() { Build(); base.Refresh(); }

    private static string Mhz(double? v) => v is double d && d > 0 ? $"{Math.Round(d)} {Loc.I["mhz"]}" : Dash;
    private static string Mhz(int v) => v > 0 ? $"{v} {Loc.I["mhz"]}" : Dash;
    private static string Txt(string s) => string.IsNullOrWhiteSpace(s) ? Dash : s;

    private void Build()
    {
        var i = _m.Info; var L = Loc.I;
        _cpuClk = new HwRowVm("clk_now", Dash); _cpuPw = new HwRowVm("pw_now", Dash);
        _gpuClk = new HwRowVm("gc", Mhz(i.GpuCoreMhz)); _gpuMem = new HwRowVm("gm", Mhz(i.GpuMemMhz));
        _vram = new HwRowVm("vram", i.VramMb > 0
            ? Corvite.Core.Hardware.HardwareClassifier.FormatMemory(i.VramMb, L["gb"], "MB")
            : Txt(i.Vram));

        var r = i.Ram;
        var speed = r.SpeedMts > 0 ? $"{r.SpeedMts} MT/s" + (r.Profile.Length > 0 ? $" ({r.Profile})" : "") : Dash;

        Cards = new List<HwCardVm>
        {
            new() { Title = $"{L["n_cpu"]} · {Txt(i.CpuName)}", Rows = new()
            {
                new("cores", Txt(i.Cores)), new("base", Mhz(i.BaseMhz)), new("boost", Mhz(i.BoostMhz)),
                new("cache", Txt(i.Cache)), new("sock", Txt(i.Socket)),
                new("tdp", i.TdpW > 0 ? $"{i.TdpW} {L["w"]}" : Dash), _cpuClk, _cpuPw
            } },
            new() { Title = $"{L["n_gpu"]} · {Txt(i.GpuName)}", Rows = new()
            {
                _vram, _gpuClk, _gpuMem, new("sup", i.GpuSupported ? L["sup"] : L["nosup"])
            } },
            new() { Title = "RAM · " + (r.Vendor.Length > 0 ? r.Vendor : Dash), Rows = new()
            {
                new("cap", r.Gb > 0 ? $"{r.Gb:0.#} {L["gb"]}" : Dash), new("ty", Txt(r.Type)), new("sp", speed),
                new("stk", r.Modules > 0 ? r.Modules.ToString() : Dash)
            } },
            new() { Title = $"{L["mb"]} / {L["dk"]}", Rows = new() { new("mb", Txt(i.Board)), new("dk", Txt(i.Disk)) } },
        };

        Demos = _m.DemoConfigs.Where(d => d.IsLaptop == (_m.Platform == Corvite.Core.Catalog.PlatformKind.Laptop))
            .Select(d => new DemoItemVm
            {
                Info = d, IsSelected = d.Id == i.Id,
                Cpu = System.Text.RegularExpressions.Regex.Replace(d.CpuName, "^(AMD|Intel) ", ""),
                Gpu = System.Text.RegularExpressions.Regex.Replace(d.GpuName, "^(NVIDIA GeForce|AMD Radeon) ", "")
            }).ToList();

        if (_m.LastSample is { } s) ApplyLive(s);
    }

    /// <summary>Updates only the live rows (called on every sensor tick, no rebuild).</summary>
    public void UpdateLive(Corvite.Hardware.SensorSample s) => ApplyLive(s);

    private void ApplyLive(Corvite.Hardware.SensorSample s)
    {
        if (_cpuClk != null) _cpuClk.Value = Mhz(s.CpuClockMhz);
        if (_cpuPw != null) _cpuPw.Value = s.CpuPowerW is double w && w > 0 ? $"{w:0.#} {Loc.I["w"]}" : Dash;
        if (_gpuClk != null && s.GpuCoreMhz is double gc && gc > 0) _gpuClk.Value = Mhz(gc);
        if (_gpuMem != null && s.GpuMemMhz is double gm && gm > 0) _gpuMem.Value = Mhz(gm);
        if (_vram != null && s.GpuVramMb is double v && v > 0 && !_m.Info.IsDemo)
            _vram.Value = Corvite.Core.Hardware.HardwareClassifier.FormatMemory((int)v, Loc.I["gb"], "MB");
    }
}


// ---------------------------------------------------------------- RAM timings (stage 6, reference only)

public sealed class RamRowVm
{
    private readonly RamRow _r;
    private readonly string _bios;
    public RamRowVm(RamRow r, string bios) { _r = r; _bios = bios; }

    private int Zone => _r.WithinKit ? 0 : _r.Level == 1 ? 1 : 2;
    public string Level => Loc.I["t" + _r.Level];
    public string Sub => Loc.I[_r.WithinKit ? "r_ok" : "r_over"];
    public System.Windows.Media.Brush Brush => Theming.Palette.Risk(Zone);
    public string Speed => "~" + _r.Speed + " MT/s";
    public string Timings => _r.Timings;
    public string Dram => "~" + _r.Dram;
    public string Ctrl => _r.Ctrl;
    public string Bios => _bios;
}

public sealed class RamTimingsPage : PageVm
{
    private readonly MainViewModel _m;
    public RamTimingsPage(MainViewModel m) { _m = m; }

    public string Title => Loc.I["n_ramt"];
    public string Description => Loc.I["d_ramt"];

    private RamInfo Ram => _m.Info.Ram;
    public bool HasData => RamTimings.NormalizeType(Ram.Type) != null;
    public bool NoData => !HasData;
    public string NoDataText => Loc.I["r_na"];

    public string Caption
    {
        get
        {
            const string dash = "—";
            var parts = new List<string>
            {
                Ram.Vendor.Length > 0 ? Ram.Vendor : dash,
                Ram.Gb > 0 ? Ram.Gb.ToString("0.#") + " " + Loc.I["gb"] : dash,
                Ram.Type.Length > 0 ? Ram.Type + (Ram.SpeedMts > 0 ? "-" + Ram.SpeedMts : "") : dash,
                _m.Info.CpuName.Length > 0 ? _m.Info.CpuName : dash
            };
            return string.Join(" · ", parts);
        }
    }

    public List<RamRowVm> Rows
    {
        get
        {
            var laptop = _m.Platform == Corvite.Core.Catalog.PlatformKind.Laptop;
            var bios = RamTimings.BiosPath(_m.Info.Board, laptop) ?? Loc.I["lap_bios"];
            return RamTimings.For(Ram, _m.Info.CpuBrand).Select(r => new RamRowVm(r, bios)).ToList();
        }
    }

    public string Note => Loc.I["r_note"] + (_m.Platform == Corvite.Core.Catalog.PlatformKind.Laptop ? " " + Loc.I["lap_ram"] : "");
    public string ChipTitle => Loc.I["r_chip_t"];
    public string ChipNote => Loc.I[RamTimings.ChipNoteKey(Ram)];
    public string HeadF => Loc.I["r_f"];
    public string HeadT => Loc.I["r_t"];
    public string HeadV => Loc.I["r_v"];
    public string HeadI => Loc.I["r_i"];
    public string HeadB => Loc.I["r_b"];
}
