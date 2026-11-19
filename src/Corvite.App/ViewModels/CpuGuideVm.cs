using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Corvite.App.Localization;
using Corvite.Core.Hardware;

namespace Corvite.App.ViewModels;

public sealed record SocketItem(string Id, string Label);
public sealed record CpuItem(string Id, string Label);
public sealed record GuideRowVm(string Param, string Safe, string Game, string Max);

/// <summary>
/// "Processor tuning guide" card on the Processor page: socket and processor drop-downs (filtered by the device type)
/// and a table of values to enter in BIOS or the manufacturer's program. Nothing is written to the machine.
/// </summary>
public sealed partial class CpuGuideVm : ObservableObject
{
    private readonly Func<bool> _laptop;
    private readonly Func<string> _cpuName;
    private readonly Action<string> _save;
    private string? _saved;
    private bool _busy;

    public CpuGuideVm(Func<bool> laptop, Func<string> cpuName, string? saved, Action<string> save)
    {
        _laptop = laptop; _cpuName = cpuName; _saved = saved; _save = save;
        Reload();
    }

    public List<SocketItem> Sockets { get; private set; } = new();
    public List<CpuItem> Cpus { get; private set; } = new();
    public List<GuideRowVm> Rows { get; private set; } = new();
    public List<string> Notes { get; private set; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string? _socketId;
    [ObservableProperty] private string? _cpuId;

    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;

    public string Specs { get; private set; } = "";
    public string Where { get; private set; } = "";
    public string Detected { get; private set; } = "";
    public bool HasDetected => Detected.Length > 0;
    public bool HasCpu => Rows.Count > 0;
    public bool NoCpu => !HasCpu;

    /// <summary>Rebuilds the lists for the current device type; keeps the saved choice, otherwise picks the installed processor.</summary>
    public void Reload()
    {
        _busy = true;
        var laptop = _laptop();
        Sockets = CpuDb.Sockets(laptop).Select(s => new SocketItem(s, SocketLabel(s))).ToList();

        var cur = (CpuId != null ? CpuDb.Find(CpuId) : null) ?? (_saved != null ? CpuDb.Find(_saved) : null);
        if (cur == null || cur.IsLaptop != laptop) cur = CpuDb.Detect(_cpuName(), laptop);
        var detected = CpuDb.Detect(_cpuName(), laptop);
        Detected = detected != null && cur?.Id == detected.Id ? detected.N : "";

        SocketId = cur?.S ?? (SocketId != null && Sockets.Any(x => x.Id == SocketId) ? SocketId : null);
        Cpus = SocketId == null ? new() : CpuDb.ForSocket(SocketId, laptop).Select(c => new CpuItem(c.Id, c.N)).ToList();
        CpuId = cur?.Id;
        _busy = false;
        Build();
    }

    public void Refresh() => Build();

    private static string SocketLabel(string s) => s switch
    {
        "AM4" or "AM5" => s + " · AMD",
        "Mobile AMD" => "AMD Ryzen Mobile",
        "Mobile Intel" => "Intel Core Mobile",
        "LGA1151" => "LGA1151 · Intel Core 9",
        "LGA1200" => "LGA1200 · Intel Core 10–11",
        "LGA1700" => "LGA1700 · Intel Core 12–14",
        "LGA1851" => "LGA1851 · Core Ultra 200",
        _ => s
    };

    partial void OnSocketIdChanged(string? value)
    {
        if (_busy) return;
        _busy = true;
        Cpus = value == null ? new() : CpuDb.ForSocket(value, _laptop()).Select(c => new CpuItem(c.Id, c.N)).ToList();
        CpuId = null;
        Detected = "";
        _busy = false;
        Build();
    }

    partial void OnCpuIdChanged(string? value)
    {
        if (_busy) return;
        if (value != null) { _saved = value; _save(value); }
        var d = CpuDb.Detect(_cpuName(), _laptop());
        Detected = d != null && d.Id == value ? d.N : "";
        Build();
    }

    private void Build()
    {
        var c = CpuId == null ? null : CpuDb.Find(CpuId);
        if (c == null)
        {
            Rows = new(); Notes = new(); Specs = ""; Where = "";
        }
        else
        {
            var g = CpuGuide.For(c, k => Loc.I[k]);
            Rows = g.Rows.Select(r => new GuideRowVm(Loc.I[r.ParamKey], r.Safe, r.Game, r.Max)).ToList();
            Notes = g.NoteKeys.Select(k => Loc.I[k]).ToList();
            Where = Loc.I[g.WhereKey];
            Specs = string.Format(Loc.I["cg.spec"], c.C, c.T, c.B.ToString("0.0#"), c.X.ToString("0.0#"), c.D, c.R);
        }
        OnPropertyChanged(string.Empty);
    }
}
