using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Corvite.App.Localization;
using Corvite.App.Services;
using Corvite.App.Theming;
using Corvite.Core.Catalog;
using Corvite.Core.Engine;
using Corvite.Core.Hardware;
using Corvite.Hardware;
using Corvite.Tweaks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Corvite.App.ViewModels;

public sealed record RedItem(string Name, string Value);

/// <summary>
/// Shell logic (TZ 7.2, 7.4, 8): pending changes, apply with red-zone confirmation, undo stack, discard,
/// section reset, profiles, PC/laptop, live language switching, persisted settings (stage 1) plus
/// hardware detection and live sensors (stage 2).
/// Stage 3: real system writes through Corvite.Tweaks (baseline, restore point, undo, overheat rollback).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private Dictionary<string, double> _s;
    private Dictionary<string, double> _a;
    private sealed record HistEntry(Dictionary<string, string> Snaps, Dictionary<string, double> Prior);
    private readonly Stack<HistEntry> _hist = new();
    private readonly TweakEngine _engine = new(Environment.GetCommandLineArgs().Contains("--dry-run"));
    private BaselineData? _baseline;
    private Dictionary<string, double> _base = new();
    private bool _rpDone;
    private DateTime _lastApply = DateTime.MinValue;
    private int _hotTicks;
    private GameSession? _game;
    private readonly Dictionary<string, TweakVm> _vms = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(2800) };
    private readonly DispatcherTimer _monTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly Random _rnd = new();
    private double _cv = 48, _gv = 52, _rv = 11.2, _cl = 34, _gl = 41;
    private string _profile = "stock";
    private SensorService? _sensors;
    private bool _live;

    // ---------------------------------------------------------------- hardware (stage 2)
    public HardwareInfo Info { get; private set; } = HardwareInfo.Placeholder;
    public HardwareProfile Hw { get; private set; } = HardwareInfo.Placeholder.ToProfile();
    public PlatformKind Platform { get; private set; } = PlatformKind.Pc;
    public SensorSample? LastSample { get; private set; }

    /// <summary>Debug demo configurations, enabled with the "--demo" command-line switch.</summary>
    public IReadOnlyList<HardwareInfo> DemoConfigs { get; } = LoadDemo();
    public bool DemoEnabled => DemoConfigs.Count > 0;

    private static IReadOnlyList<HardwareInfo> LoadDemo()
    {
        if (!Environment.GetCommandLineArgs().Contains("--demo")) return Array.Empty<HardwareInfo>();
        try { return DemoHardware.Load(); } catch { return Array.Empty<HardwareInfo>(); }
    }

    public ObservableCollection<SectionVm> Sections { get; } = new();
    public List<ProfileVm> Profiles { get; } = new();
    public List<TweakVm> GuardTweaks { get; } = new();
    public MonitorCardVm CpuCard { get; } = new("ov.cpu", "°C", 30, 95);
    public MonitorCardVm GpuCard { get; } = new("ov.gpu", "°C", 30, 95);
    public MonitorCardVm RamCard { get; } = new("ov.ram", "gb", 0, 32, unitIsKey: true);

    [ObservableProperty] private PageVm? _currentPage;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private bool _hwLoading = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _bkDelArmed;

    // "needs a manufacturer program" dialog
    [ObservableProperty] private bool _toolVisible;
    private VendorTool? _tool;
    private TweakDef? _toolFor;
    public string ToolName => _tool == null ? "" : Loc.I[_tool.NameKey];
    public string ToolWhy => _tool == null ? "" : Loc.I[_tool.WhyKey];
    public string ToolSource => _tool == null ? "" : _tool.Host;
    public string ToolDevice => _toolFor == null ? "" : _toolFor.Section == Section.Gpu
        ? (Info.GpuName.Length > 0 ? Info.GpuName : "—") : (Info.CpuName.Length > 0 ? Info.CpuName : "—");
    public string ToolTweak => _toolFor == null ? "" : _vms[_toolFor.Id].Title;

    public void ShowToolInfo(string id)
    {
        var def = TweakCatalog.All.FirstOrDefault(d => d.Id == id);
        if (def == null) return;
        _tool = VendorTools.Resolve(def.Section, Info, _engine.IsDriverBound(id));
        _toolFor = def;
        if (_tool == null || !_tool.IsOfficial) { Toast(Loc.I["tool.none"]); return; }
        OnPropertyChanged(nameof(ToolName)); OnPropertyChanged(nameof(ToolWhy)); OnPropertyChanged(nameof(ToolSource));
        OnPropertyChanged(nameof(ToolDevice)); OnPropertyChanged(nameof(ToolTweak));
        ToolVisible = true;
    }

    [RelayCommand]
    private void ToolOpen()
    {
        if (_tool is not { IsOfficial: true }) return;
        try { Process.Start(new ProcessStartInfo(_tool.Url) { UseShellExecute = true }); } catch { /* no browser */ }
    }

    [RelayCommand]
    private void ToolClose() => ToolVisible = false;

    [RelayCommand]
    private void ThaiphoonOpen()
    {
        var t = new VendorTool("", "", VendorTools.ThaiphoonUrl);
        if (!t.IsOfficial) return;
        try { Process.Start(new ProcessStartInfo(t.Url) { UseShellExecute = true }); } catch { /* no browser */ }
    }

    // ---------------------------------------------------------------- RAM tab: chosen chip
    /// <summary>null = never chosen (the part-number hint may apply), "" = averaged table chosen explicitly.</summary>
    public string? RamChipChoice => _settings.RamChip;

    [RelayCommand]
    private void PickRamChip(string? id)
    {
        _settings.RamChip = id ?? "";
        SettingsStore.Save(_settings);
        (CurrentPage as RamTimingsPage)?.Refresh();
    }


    // red-zone dialog
    [ObservableProperty] private bool _redVisible;
    [ObservableProperty] private bool _redAck;
    public List<RedItem> RedItems { get; private set; } = new();

    // platform dialog: 0 hidden, 1 choose, 2 warning
    [ObservableProperty] private int _platStep;
    [ObservableProperty] private PlatformKind? _platChoice;
    [ObservableProperty] private bool _platAutoHint;
    [ObservableProperty] private PlatformKind? _platDetected; // null = no evidence, no recommendation

    public MainViewModel()
    {
        // persisted choices
        Loc.I.Set(_settings.Language);
        if (_settings.Platform != null) Platform = _settings.Platform == "laptop" ? PlatformKind.Laptop : PlatformKind.Pc;

        _s = ProfileEngine.Preset("stock", Platform);
        _a = new Dictionary<string, double>(_s);

        foreach (var d in TweakCatalog.All) _vms[d.Id] = new TweakVm(d, this);
        foreach (var d in TweakCatalog.All.Where(x => x.IsGuard)) GuardTweaks.Add(_vms[d.Id]);

        void Sec(Section id, string key, string icon) => Sections.Add(new SectionVm(id, key, icon));
        Sec(Section.Overview, "overview", "M3,3 h8 v8 h-8 z M13,3 h8 v5 h-8 z M13,11 h8 v10 h-8 z M3,14 h8 v7 h-8 z");
        Sec(Section.Hardware, "hardware", "M4,4 h16 v12 h-16 z M8,20 h8 M12,16 v4");
        Sec(Section.Windows, "system", "M4,7 h10 M18,7 h2 M4,17 h2 M10,17 h10 M14,7 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M6,17 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0");
        Sec(Section.Cpu, "cpu", "M7,7 h10 v10 h-10 z M10,3 v4 M14,3 v4 M10,17 v4 M14,17 v4 M3,10 h4 M3,14 h4 M17,10 h4 M17,14 h4");
        Sec(Section.Gpu, "gpu", "M2.5,6 h19 v11 h-19 z M6,20 h12 M16,9.5 h2.5 M16,13.5 h2.5 M12,11.5 a3,3 0 1 0 -6,0 a3,3 0 1 0 6,0");
        Sec(Section.Memory, "memory", "M3,8 h18 v8 h-18 z M7,16 v3 M11,16 v3 M15,16 v3 M7,11 v2 M11,11 v2 M15,11 v2");
        Sec(Section.RamTimings, "ramt", "M4,6 h16 M4,12 h16 M4,18 h16 M8,3 v6 M16,9 v6 M10,15 v6");
        Sec(Section.Network, "network", "M13,5 a2,2 0 1 0 -4,0 a2,2 0 1 0 4,0 M7,18 a2,2 0 1 0 -4,0 a2,2 0 1 0 4,0 M21,18 a2,2 0 1 0 -4,0 a2,2 0 1 0 4,0 M11,7 l-5,9 M13,7 l5,9 M7,18 h10");
        Sec(Section.Background, "background", "M12,3 l9,5 l-9,5 l-9,-5 z M3,13 l9,5 l9,-5");

        Profiles.Add(new ProfileVm("stock", 0));
        Profiles.Add(new ProfileVm("safe", 1));
        Profiles.Add(new ProfileVm("game", 2));
        Profiles.Add(new ProfileVm("max", 4));

        // monitoring: demo data until the first real sensor sample arrives
        for (int i = 0; i < 40; i++) Tick();
        _monTimer.Tick += (_, _) => Tick();
        _monTimer.Start();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };

        Loc.I.LanguageChanged += RefreshAll;
        Navigate(Section.Overview);
        PlatStep = _settings.Platform == null ? 1 : 0; // first launch: choose PC / laptop (TZ 7.4)

        StartHardwareDetection();
        StartSensors();
    }

    // ---------------------------------------------------------------- hardware detection / sensors
    private async void StartHardwareDetection()
    {
        HardwareInfo info;
        try { info = await Task.Run(HardwareDetector.Detect); }
        catch { info = HardwareInfo.Placeholder; }

        HwLoading = false;
        ApplyHardware(info);

        if (info.Id == "detected" && info.DeviceHint.Length > 0)
        {
            _deviceHint = info.DeviceHint;
            PlatDetected = info.IsLaptop ? PlatformKind.Laptop : PlatformKind.Pc;
        }

        // suggest the detected device type on first launch (the user still confirms it)
        if (_settings.Platform == null && PlatStep == 1 && PlatChoice == null && PlatDetected != null)
        {
            PlatChoice = PlatDetected;
            PlatAutoHint = true;
        }

        await SyncSystemAsync();
    }

    // ---------------------------------------------------------------- real system state (stage 3)
    public bool DryRun => _engine.DryRun;

    /// <summary>A tweak is usable when it has a real implementation that fits this machine (guards are app features).</summary>
    public bool IsSupported(string id) => _vms[id].IsGuard || _engine.IsAvailable(id, Info);
    public string LockKey(string id) => _engine.Has(id) ? "lock.na" : _engine.IsDriverBound(id) ? "lock.drv" : "lock.soon";

    private async Task SyncSystemAsync()
    {
        var hw = Info;
        bool created = false;
        Dictionary<string, double> vals;
        BaselineData? b;
        try
        {
            (vals, b, created) = await Task.Run(() =>
            {
                var v = _engine.ReadAll(hw);
                var bl = BaselineStore.Load();
                bool c = false;
                if (bl == null) { bl = _engine.CreateBaseline(hw); c = true; }
                return (v, bl, c);
            });
        }
        catch (Exception ex)
        {
            Log.Info("sync failed: " + ex.Message);
            return;
        }

        if (b == null) return;
        _baseline = b;
        _base = new Dictionary<string, double>(b.Values);
        foreach (var kv in vals) SetBoth(kv.Key, kv.Value);
        _profile = DetectProfileLocal();
        RefreshAll();
        if (created) Toast(Loc.I["bk_cr"]);
        StartGameSession();

        // NVIDIA card but no driver library: tell the user which official driver is needed
        if (Info.GpuBrand == GpuVendor.Nvidia && !await Task.Run(() => Prerequisites.NvidiaRuntimePresent))
            ShowToolInfo("gpow");
    }

    private void StartGameSession()
    {
        if (_game != null) return;
        _game = new GameSession(_engine.DryRun);
        _game.GameStarted += n => Application.Current?.Dispatcher.InvokeAsync(() => Toast(Loc.I["game.on"] + n));
        _game.GameEnded += n => Application.Current?.Dispatcher.InvokeAsync(() => Toast(Loc.I["game.off"] + n));
        _game.Start();
    }

    private void SetBoth(string id, double v)
    {
        var d = TweakCatalog.All.FirstOrDefault(x => x.Id == id);
        if (d == null) return;
        v = d.IsToggle ? (v >= 0.5 ? 1 : 0) : Math.Clamp(v, d.Min, ProfileEngine.EffectiveMax(d, Platform));
        _s[id] = v; _a[id] = v;
    }

    private double BaseVal(TweakDef d) => _base.TryGetValue(d.Id, out var v) ? v : ProfileEngine.Preset("stock", Platform)[d.Id];

    /// <summary>Like ProfileEngine.DetectProfile, but locked tweaks are ignored and "stock" means the saved baseline.</summary>
    private string DetectProfileLocal()
    {
        var ids = Visible.Where(d => !d.IsGuard && IsSupported(d.Id)).ToList();
        if (ids.All(d => Math.Abs(_s[d.Id] - BaseVal(d)) < 1e-9)) return "stock";
        foreach (var p in new[] { "safe", "game", "max" })
        {
            var pr = ProfileEngine.Preset(p, Platform);
            if (ids.All(d => Math.Abs(_s[d.Id] - pr[d.Id]) < 1e-9)) return p;
        }
        return ProfileEngine.Custom;
    }

    /// <summary>Switches the active hardware profile (detected or demo) and rebuilds the visible page.</summary>
    private void ApplyHardware(HardwareInfo info)
    {
        Info = info;
        Hw = info.ToProfile();
        // vendor-specific tweaks of the previous hardware must not leave pending changes behind
        foreach (var d in TweakCatalog.All.Where(d => d.Vendor != Vendor.Any)) _s[d.Id] = _a[d.Id];
        _profile = DetectProfileLocal();
        Navigate(_cur);
    }

    [RelayCommand]
    private void SelectDemo(DemoItemVm item) => ApplyHardware(item.Info);

    private void StartSensors()
    {
        Task.Run(() =>
        {
            try
            {
                var svc = new SensorService();
                svc.SampleReady += s => Application.Current?.Dispatcher.InvokeAsync(() => OnSample(s));
                svc.Start();
                _sensors = svc;
            }
            catch { /* sensors unavailable: keep demo data */ }
        });
    }

    private void OnSample(SensorSample s)
    {
        if (!_live) { _live = true; _monTimer.Stop(); }
        LastSample = s;
        var l = Loc.I;
        string Load(double? v) => v is double d ? $"{l["ld_now"]} {Math.Round(d)}%" : l["nosensor"];
        string Temp(double? v) => v is double d ? Math.Round(d).ToString() : "—";

        CpuCard.Push(s.CpuTemp, Load(s.CpuLoad), Temp(s.CpuTemp), false);
        GpuCard.Push(s.GpuTemp, Load(s.GpuLoad), Temp(s.GpuTemp), false);
        RamCard.SetRange(0, s.RamTotalGb);
        RamCard.Push(s.RamUsedGb, $"{l["ov.of"]}{Math.Round(s.RamTotalGb)} {l["gb"]}", s.RamUsedGb.ToString("0.0"), false);

        (CurrentPage as HardwarePage)?.UpdateLive(s);
        Watchdog(s);
    }

    public void Shutdown()
    {
        try { _sensors?.Dispose(); } catch { /* ignore */ }
        try { _game?.Dispose(); } catch { /* ignore */ }
    }

    // ---------------------------------------------------------------- left rail summary
    public string RailTitle => Loc.I[Platform == PlatformKind.Laptop ? "hw_lap" : "hw_pc"];
    public string RailCpu => Short(Info.CpuName, "^(AMD|Intel) ");
    public string RailGpu => Short(Info.GpuName, "^(NVIDIA GeForce|AMD Radeon|Intel) ");
    public string RailRam => Info.Ram.Gb > 0 ? $"{Info.Ram.Gb:0.#} {Loc.I["gb"]} {Info.Ram.Type}".Trim() : "—";
    private static string Short(string s, string pattern) => s.Length == 0 ? "—" : Regex.Replace(s, pattern, "");

    // ---------------------------------------------------------------- state access
    public double GetValue(string id) => _s[id];
    public double GetApplied(string id) => _a[id];

    public void SetValue(string id, double v)
    {
        _s[id] = v;
        if (!_vms[id].IsGuard) _profile = ProfileEngine.Custom;
        RefreshAll();
    }

    private IEnumerable<TweakDef> Visible => TweakCatalog.All.Where(d => ProfileEngine.IsVisible(d, Hw));
    private List<TweakDef> Changed() => Visible.Where(d => IsSupported(d.Id) && Math.Abs(_s[d.Id] - _a[d.Id]) > 1e-9).ToList();

    // ---------------------------------------------------------------- navigation
    private Section _cur = Section.Overview;

    [RelayCommand]
    private void Select(SectionVm sec) => Navigate(sec.Id);

    private void Navigate(Section id)
    {
        _cur = id;
        var sec = Sections.First(x => x.Id == id);
        foreach (var s in Sections) s.IsSelected = s.Id == id;
        CurrentPage = id switch
        {
            Section.Overview => new OverviewPage(this),
            Section.Hardware => new HardwarePage(this),
            Section.RamTimings => new RamTimingsPage(this),
            Section.Gpu when Hw.Gpu is GpuVendor.IntelArc or GpuVendor.Unknown => new PlannedPage(sec, "unsup"),
            _ => BuildSectionPage(sec)
        };
        RefreshAll();
    }

    private SectionPage BuildSectionPage(SectionVm sec)
    {
        var groups = new List<GroupVm>();
        var defs = TweakCatalog.All.Where(d => d.Section == sec.Id && !d.IsGuard && ProfileEngine.IsVisible(d, Hw)).ToList();
        foreach (var g in defs.Select(d => d.GroupKey).Distinct())
            groups.Add(new GroupVm(g, defs.Where(d => d.GroupKey == g).Select(d => _vms[d.Id]).ToList()));
        return new SectionPage(sec, groups, ResetSectionCommand);
    }

    [RelayCommand]
    private void ResetSection()
    {
        foreach (var d in TweakCatalog.All.Where(d => d.Section == _cur && !d.IsGuard && IsSupported(d.Id))) _s[d.Id] = BaseVal(d);
        _profile = DetectProfileLocal();
        RefreshAll();
    }

    // ---------------------------------------------------------------- profiles
    [RelayCommand]
    private void SelectProfile(ProfileVm p)
    {
        var pr = ProfileEngine.Preset(p.Id, Platform);
        foreach (var d in TweakCatalog.All.Where(d => !d.IsGuard && IsSupported(d.Id)))
            _s[d.Id] = p.Id == "stock" ? BaseVal(d) : pr[d.Id];
        _profile = p.Id;
        RefreshAll();
        Toast(Loc.I["toast.prof1"] + p.Name + Loc.I["toast.prof2"]);
    }

    // ---------------------------------------------------------------- apply / discard / undo
    public int ChangedCount => Changed().Count;
    public bool HasChanges => ChangedCount > 0;
    public bool NotBusy => !IsBusy;
    public bool CanApply => HasChanges && !IsBusy;
    public bool CanRevert => HasChanges && !IsBusy;
    public bool CanUndo => _hist.Count > 0 && !IsBusy;
    public string PendingText => IsBusy ? Loc.I["busy"] : HasChanges ? Loc.I["pend"] + ChangedCount : Loc.I["done"];

    partial void OnIsBusyChanged(bool value) => RefreshAll();

    [RelayCommand]
    private void Apply()
    {
        if (IsBusy) return;
        var red = Changed().Where(d => ProfileEngine.IsRed(d, _s[d.Id], Platform)).ToList();
        if (red.Count == 0) { DoApply(); return; }
        RedItems = red.Select(d => new RedItem(_vms[d.Id].Title, d.IsToggle ? Loc.I["on"] : _vms[d.Id].ValueText)).ToList();
        OnPropertyChanged(nameof(RedItems));
        RedAck = false;
        RedVisible = true;
    }

    [RelayCommand(CanExecute = nameof(RedAck))]
    private void RedConfirm() { RedVisible = false; DoApply(); }

    partial void OnRedAckChanged(bool value) => RedConfirmCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void RedCancel() => RedVisible = false;

    private async void DoApply()
    {
        if (IsBusy) return;
        var changed = Changed();
        var req = changed.Where(d => !d.IsGuard).ToDictionary(d => d.Id, d => _s[d.Id]);
        foreach (var d in changed.Where(d => d.IsGuard)) _a[d.Id] = _s[d.Id]; // app settings: nothing to write to Windows
        if (req.Count == 0) { RefreshAll(); Toast(Loc.I["toast.applied"] + changed.Count + Loc.I["toast.rp"]); return; }

        IsBusy = true;
        ApplyReport rep;
        try
        {
            bool makeRp = _s["g_rest"] >= 0.5 && !_rpDone && !_engine.DryRun;
            rep = await Task.Run(() =>
            {
                if (makeRp) RestorePoint.Create("Corvite");
                return _engine.Apply(req);
            });
            if (makeRp) _rpDone = true;
        }
        catch (Exception ex)
        {
            Log.Info("apply crashed: " + ex);
            IsBusy = false;
            Toast(Loc.I["toast.fail"] + ex.Message);
            return;
        }

        var prior = new Dictionary<string, double>();
        foreach (var id in rep.Done) { prior[id] = _a[id]; _a[id] = _s[id]; }
        foreach (var id in rep.Failed.Keys) _s[id] = _a[id]; // show the truth: it did not change
        if (rep.Done.Count > 0)
        {
            _hist.Push(new HistEntry(rep.Before.Where(kv => rep.Done.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), prior));
            _lastApply = DateTime.Now;
            _hotTicks = 0;
        }
        _profile = DetectProfileLocal();
        IsBusy = false;

        var msg = rep.Failed.Count > 0
            ? Loc.I["toast.fail"] + string.Join(", ", rep.Failed.Keys.Select(k => _vms[k].Title))
            : Loc.I["toast.applied"] + rep.Done.Count + Loc.I["toast.rp"];
        if (rep.NeedsReboot && rep.Failed.Count == 0) msg += " · " + Loc.I["toast.reboot"];
        Toast(msg);
    }

    [RelayCommand]
    private void Revert()
    {
        _s = new Dictionary<string, double>(_a);
        _profile = DetectProfileLocal();
        RefreshAll();
        Toast(Loc.I["toast.discard"]);
    }

    [RelayCommand]
    private async Task Undo() => await UndoCore(Loc.I["undo_t"]);

    private async Task UndoCore(string toastPrefix)
    {
        if (_hist.Count == 0 || IsBusy) return;
        var h = _hist.Pop();
        IsBusy = true;
        Dictionary<string, string> failed;
        try { failed = await Task.Run(() => _engine.Restore(h.Snaps)); }
        catch (Exception ex) { failed = h.Snaps.Keys.ToDictionary(k => k, _ => ex.Message); }

        foreach (var kv in h.Prior)
            if (!failed.ContainsKey(kv.Key)) SetBoth(kv.Key, kv.Value);
        _profile = DetectProfileLocal();
        IsBusy = false;
        Toast(failed.Count > 0 ? Loc.I["toast.fail"] + string.Join(", ", failed.Keys.Select(k => _vms[k].Title))
                               : toastPrefix + _hist.Count);
    }

    /// <summary>Overheat watchdog (TZ 7.1): two consecutive hot samples soon after an apply roll the last change back.</summary>
    private void Watchdog(SensorSample s)
    {
        if (IsBusy || _hist.Count == 0 || _s["g_roll"] < 0.5) return;
        if (DateTime.Now - _lastApply > TimeSpan.FromMinutes(15)) return;
        var t = Math.Max(s.CpuTemp ?? 0, s.GpuTemp ?? 0);
        _hotTicks = t >= _s["g_temp"] ? _hotTicks + 1 : 0;
        if (_hotTicks >= 2)
        {
            _hotTicks = 0;
            Log.Info($"watchdog rollback at {t:0} °C");
            _ = UndoCore(Loc.I["toast.rollback"] + " · ");
        }
    }

    // ---------------------------------------------------------------- backup of original settings (TZ 13)
    public bool BkExists => _baseline != null;
    public bool BkMissing => _baseline == null;
    public string BkWhen => _baseline == null ? "" : _baseline.Created.ToString("dd.MM.yyyy HH:mm");
    public string BkFile => BaselineStore.FilePath;
    public string BkDelText => Loc.I[BkDelArmed ? "bk_sure" : "bk_del"];
    partial void OnBkDelArmedChanged(bool value) => OnPropertyChanged(nameof(BkDelText));

    [RelayCommand]
    private async Task BackupRestore()
    {
        if (_baseline == null || IsBusy) return;
        IsBusy = true;
        Dictionary<string, string> failed;
        try { failed = await Task.Run(() => _engine.Restore(_baseline.Snapshots)); }
        catch (Exception ex) { failed = new() { ["all"] = ex.Message }; }
        foreach (var kv in _base) if (!failed.ContainsKey(kv.Key)) SetBoth(kv.Key, kv.Value);
        _hist.Clear();
        _profile = DetectProfileLocal();
        IsBusy = false;
        Toast(failed.Count > 0 ? Loc.I["toast.fail"] + string.Join(", ", failed.Keys) : Loc.I["bk_done"]);
    }

    [RelayCommand]
    private void BackupOpen()
    {
        try
        {
            AppPaths.Ensure(AppPaths.BackupDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.BackupDir}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task BackupNew()
    {
        if (IsBusy) return;
        IsBusy = true;
        var hw = Info;
        try
        {
            _baseline = await Task.Run(() => _engine.CreateBaseline(hw));
            _base = new Dictionary<string, double>(_baseline.Values);
            Toast(Loc.I["bk_cr"]);
        }
        catch (Exception ex) { Toast(Loc.I["toast.fail"] + ex.Message); }
        IsBusy = false;
    }

    [RelayCommand]
    private void BackupDelete()
    {
        if (!BkDelArmed) { BkDelArmed = true; return; }
        BkDelArmed = false;
        BaselineStore.Delete();
        _baseline = null;
        RefreshAll();
        Toast(Loc.I["bk_deleted"]);
    }

    // ---------------------------------------------------------------- language
    [RelayCommand]
    private void SetLang(string lang)
    {
        Loc.I.Set(lang);
        _settings.Language = lang;
        SettingsStore.Save(_settings);
    }

    public string Lang => Loc.I.Lang;
    public bool IsRu => Loc.I.Lang == "ru";
    public bool IsUk => Loc.I.Lang == "uk";
    public bool IsEn => Loc.I.Lang == "en";

    // ---------------------------------------------------------------- platform (TZ 7.4)
    public string PlatformLabel => Loc.I[Platform == PlatformKind.Laptop ? "pl_lap" : "pl_pc"];
    public bool PlatShowChoose => PlatStep == 1;
    public bool PlatShowWarn => PlatStep == 2;
    public bool PlatVisible => PlatStep != 0;
    public bool PlatIsLaptop => PlatChoice == PlatformKind.Laptop;
    public bool PlatIsPc => PlatChoice == PlatformKind.Pc;
    public bool PlatCanNext => PlatChoice != null;
    private string _deviceHint = "";
    public bool PlatRecPc => PlatDetected == PlatformKind.Pc;
    public bool PlatRecLaptop => PlatDetected == PlatformKind.Laptop;
    /// <summary>Laptop detected but PC mode chosen: full overclock limits on laptop cooling (risk warning).</summary>
    public bool PlatMismatchRisk => PlatDetected == PlatformKind.Laptop && PlatChoice == PlatformKind.Pc;
    /// <summary>PC detected but Laptop mode chosen: harmless, limits are simply tighter.</summary>
    public bool PlatMismatchInfo => PlatDetected == PlatformKind.Pc && PlatChoice == PlatformKind.Laptop;
    public string PlatReason => _deviceHint.Length == 0 ? "" :
        Loc.I["pl_by"] + string.Join(", ", _deviceHint.Split(',').Select(h => Loc.I["sig_" + h]));
    public string PlatWarnTitle => Loc.I[PlatIsLaptop ? "wl_t" : "wp_t"];
    public string PlatWarnText => Loc.I[PlatIsLaptop ? "wl_d" : "wp_d"];

    partial void OnPlatStepChanged(int value) { RaisePlat(); }
    partial void OnPlatChoiceChanged(PlatformKind? value) { RaisePlat(); }
    partial void OnPlatDetectedChanged(PlatformKind? value) { RaisePlat(); }

    private void RaisePlat()
    {
        foreach (var n in new[] { nameof(PlatShowChoose), nameof(PlatShowWarn), nameof(PlatVisible), nameof(PlatIsLaptop),
                 nameof(PlatIsPc), nameof(PlatCanNext), nameof(PlatWarnTitle), nameof(PlatWarnText),
                 nameof(PlatRecPc), nameof(PlatRecLaptop), nameof(PlatMismatchRisk), nameof(PlatMismatchInfo), nameof(PlatReason) })
            OnPropertyChanged(n);
    }

    [RelayCommand]
    private void PlatOpen() { PlatChoice = Platform; PlatAutoHint = false; PlatStep = 1; }

    [RelayCommand]
    private void PlatPick(string kind) { PlatAutoHint = false; PlatChoice = kind == "laptop" ? PlatformKind.Laptop : PlatformKind.Pc; }

    [RelayCommand]
    private void PlatNext()
    {
        if (PlatStep == 1 && PlatChoice != null) { PlatStep = 2; return; }
        if (PlatStep == 2 && PlatChoice != null)
        {
            PlatStep = 0;
            SetPlatform(PlatChoice.Value);
            _settings.Platform = PlatChoice.Value == PlatformKind.Laptop ? "laptop" : "pc";
            SettingsStore.Save(_settings);
        }
    }

    [RelayCommand]
    private void PlatBack() => PlatStep = 1;

    private void SetPlatform(PlatformKind p)
    {
        Platform = p;
        foreach (var d in TweakCatalog.All.Where(d => !d.IsToggle))
        {
            var max = ProfileEngine.EffectiveMax(d, p);
            if (_s[d.Id] > max) _s[d.Id] = max;
            if (_a[d.Id] > max) _a[d.Id] = max;
        }
        _hist.Clear(); // changing the device type resets the undo history
        _profile = DetectProfileLocal();
        Navigate(_cur);
    }

    // ---------------------------------------------------------------- overview numbers
    public string GainText
    {
        get
        {
            var (lo, hi) = ProfileEngine.Gain(Scoring(), Hw, Platform);
            return Math.Round(hi) < 1 ? "≈ 0%" : $"~+{Math.Round(lo)}–{Math.Round(hi)}%";
        }
    }
    public string GainCaption => Loc.I["ov.gaincap"];
    private double Risk => ProfileEngine.RiskScore(Scoring(), Hw, Platform);

    /// <summary>
    /// State used for the FPS estimate and the risk level: everything still equal to the saved original (baseline) counts as
    /// "not changed by Corvite", so the Standard profile on a PC that already has something switched on shows zero gain and low risk.
    /// </summary>
    private Dictionary<string, double> Scoring()
    {
        var stock = ProfileEngine.Preset("stock", Platform);
        var r = new Dictionary<string, double>(_s);
        foreach (var d in TweakCatalog.All.Where(d => !d.IsGuard))
            if (IsSupported(d.Id) && Math.Abs(_s[d.Id] - BaseVal(d)) < 1e-9) r[d.Id] = stock[d.Id];
        return r;
    }
    public string RiskWord => Loc.I[$"tot.{ProfileEngine.RiskLevel(Risk)}"];
    public Brush RiskBrush => Palette.Total(ProfileEngine.RiskLevel(Risk));
    public GridLength RiskLeft => new(Math.Max(Risk, 0.0001), GridUnitType.Star);
    public GridLength RiskRight => new(Math.Max(1 - Risk, 0.0001), GridUnitType.Star);

    // ---------------------------------------------------------------- refresh
    private void RefreshAll()
    {
        foreach (var v in _vms.Values) v.Refresh();
        foreach (var s in Sections)
        {
            s.Refresh();
            s.Count = TweakCatalog.All.Count(d => d.Section == s.Id && !d.IsGuard && ProfileEngine.IsVisible(d, Hw)
                                                   && ProfileEngine.IsActive(d, _s[d.Id]));
        }
        foreach (var p in Profiles) { p.Refresh(); p.IsSelected = p.Id == _profile; }
        CpuCard.Refresh(); GpuCard.Refresh(); RamCard.Refresh();
        CurrentPage?.Refresh();
        OnPropertyChanged(string.Empty);
        ApplyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    private void Toast(string text)
    {
        ToastText = text; ToastVisible = true;
        _toastTimer.Stop(); _toastTimer.Start();
    }

    // ---------------------------------------------------------------- demo monitoring (until real sensors answer)
    private void Tick()
    {
        var risk = ProfileEngine.RiskScore(Scoring(), Hw, Platform);
        _cv = _cv * .82 + (46 + risk * 20 + 4) * .18 + (_rnd.NextDouble() - .5) * 2.4;
        _gv = _gv * .82 + (50 + risk * 16 + 6) * .18 + (_rnd.NextDouble() - .5) * 2.4;
        _rv = Math.Min(30, Math.Max(8, _rv + (_rnd.NextDouble() - .5) * .3));
        _cl = Math.Max(8, Math.Min(96, _cl + (_rnd.NextDouble() - .5) * 9));
        _gl = Math.Max(10, Math.Min(98, _gl + (_rnd.NextDouble() - .5) * 10));
        var load = Loc.I["ld_now"] + " ";
        CpuCard.Push(_cv, $"{load}{Math.Round(_cl)}%", Math.Round(_cv).ToString(), true);
        GpuCard.Push(_gv, $"{load}{Math.Round(_gl)}%", Math.Round(_gv).ToString(), true);
        RamCard.Push(_rv, $"{Loc.I["ov.of"]}32 {Loc.I["gb"]}", _rv.ToString("0.0"), true);
    }
}
