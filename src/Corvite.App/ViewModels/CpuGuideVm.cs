using Corvite.App.Localization;
using Corvite.Core.Hardware;

namespace Corvite.App.ViewModels;

/// <summary>"Processor tuning guide" card on the Processor page: socket and processor drop-downs filtered by the device type.</summary>
public sealed class CpuGuideVm : TuningGuideVm
{
    private readonly Func<bool> _laptop;
    private readonly Func<string> _cpuName;
    private readonly Action<string> _save;
    private string? _saved;

    public CpuGuideVm(Func<bool> laptop, Func<string> cpuName, string? saved, Action<string> save)
    {
        _laptop = laptop; _cpuName = cpuName; _saved = saved; _save = save;
        Reload();
    }

    public override string Title => Loc.I["cg.t"];
    public override string Subtitle => Loc.I["cg.sub"];
    public override string GroupLabel => Loc.I["cg.sock"];
    public override string ItemLabel => Loc.I["cg.cpu"];
    public override string EmptyText => Loc.I["cg.empty"];

    public override void Reload()
    {
        Busy = true;
        var laptop = _laptop();
        Groups = CpuDb.Sockets(laptop).Select(s => new GuideChoice(s, SocketLabel(s))).ToList();

        var cur = (ItemId != null ? CpuDb.Find(ItemId) : null) ?? (_saved != null ? CpuDb.Find(_saved) : null);
        if (cur == null || cur.IsLaptop != laptop) cur = CpuDb.Detect(_cpuName(), laptop);

        GroupId = cur?.S ?? (GroupId != null && Groups.Any(x => x.Id == GroupId) ? GroupId : null);
        Items = GroupId == null ? new() : CpuDb.ForSocket(GroupId, laptop).Select(c => new GuideChoice(c.Id, c.N)).ToList();
        ItemId = cur?.Id;
        Busy = false;
        Build();
    }

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

    protected override void GroupPicked(string? id)
    {
        Busy = true;
        Items = id == null ? new() : CpuDb.ForSocket(id, _laptop()).Select(c => new GuideChoice(c.Id, c.N)).ToList();
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
        var c = ItemId == null ? null : CpuDb.Find(ItemId);
        var det = CpuDb.Detect(_cpuName(), _laptop());
        Detected = c != null && det?.Id == c.Id ? string.Format(Loc.I["cg.det"], det.N) : "";
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
        Changed();
    }
}
