using Corvite.App.Localization;
using Corvite.App.Services;
using Corvite.Core.Hardware;

namespace Corvite.App.ViewModels;

/// <summary>"Graphics card tuning guide" card on the Graphics page: series and card drop-downs (GTX 1060 and newer, Radeon of the same class).</summary>
public sealed class GpuGuideVm : TuningGuideVm
{
    private readonly Func<string> _gpuName;
    private readonly Action<string> _save;
    private string? _saved;

    public GpuGuideVm(Func<string> gpuName, string? saved, Action<string> save)
    {
        _gpuName = gpuName; _saved = saved; _save = save;
        Reload();
    }

    public override string Title => Loc.I["gg.t"];
    public override string Subtitle => Loc.I["gg.sub"];
    public override string GroupLabel => Loc.I["gg.grp"];
    public override string ItemLabel => Loc.I["gg.card"];
    public override string EmptyText => Loc.I["gg.empty"];

    public override void Reload()
    {
        Busy = true;
        Groups = GpuDb.Series().Select(s => new GuideChoice(s, SeriesLabel(s))).ToList();
        var cur = (ItemId != null ? GpuDb.Find(ItemId) : null) ?? (_saved != null ? GpuDb.Find(_saved) : null) ?? GpuDb.Detect(_gpuName());
        GroupId = cur?.S ?? GroupId;
        Items = GroupId == null ? new() : GpuDb.ForSeries(GroupId).Select(g => new GuideChoice(g.Id, g.N)).ToList();
        ItemId = cur?.Id;
        Busy = false;
        Build();
    }

    private static string SeriesLabel(string s) => s switch
    {
        "nv10" => "GeForce GTX 10 · Pascal",
        "nv16" => "GeForce GTX 16 · Turing",
        "nv20" => "GeForce RTX 20 · Turing",
        "nv30" => "GeForce RTX 30 · Ampere",
        "nv40" => "GeForce RTX 40 · Ada Lovelace",
        "nv50" => "GeForce RTX 50 · Blackwell",
        "amdgcn" => "Radeon RX 500 / Vega",
        "amd5" => "Radeon RX 5000 · RDNA 1",
        "amd6" => "Radeon RX 6000 · RDNA 2",
        "amd7" => "Radeon RX 7000 · RDNA 3",
        "amd9" => "Radeon RX 9000 · RDNA 4",
        _ => s
    };

    protected override void GroupPicked(string? id)
    {
        Busy = true;
        Items = id == null ? new() : GpuDb.ForSeries(id).Select(g => new GuideChoice(g.Id, g.N)).ToList();
        ItemId = null;
        Busy = false;
        Build();
    }

    protected override void ItemPicked(string? id)
    {
        if (id != null) { _saved = id; _save(id); }
        Build();
    }

    protected override void Build()
    {
        var g = ItemId == null ? null : GpuDb.Find(ItemId);
        var det = GpuDb.Detect(_gpuName());
        Detected = g != null && det?.Id == g.Id ? string.Format(Loc.I["gg.det"], det.N) : "";
        if (g == null)
        {
            Rows = new(); Notes = new(); Steps = new(); Specs = ""; Where = ""; App = null;
        }
        else
        {
            var guide = GpuGuide.For(g, k => Loc.I[k]);
            Rows = guide.Rows.Select(r => new GuideRowVm(Loc.I[r.ParamKey], r.Safe, r.Game, r.Max)).ToList();
            Notes = guide.NoteKeys.Select(k => Loc.I[k]).ToList();
            Where = Loc.I[guide.WhereKey];
            Steps = Number((guide.StepKeys ?? Array.Empty<string>()).Select(k => Loc.I[k]));
            App = g.IsNvidia ? OfficialApp.NvidiaPanel : OfficialApp.AmdSoftware;
            Specs = string.Format(Loc.I["gg.spec"], g.Vram, g.Mem, g.D, g.X, g.R);
        }
        Changed();
    }
}
