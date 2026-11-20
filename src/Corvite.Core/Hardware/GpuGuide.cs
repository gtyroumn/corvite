namespace Corvite.Core.Hardware;

/// <summary>
/// Reference values for tuning a graphics card. Rules, not measurements: they start from the stock state and step away from it
/// in small, commonly stable increments. For NVIDIA, power limit and clock offsets can be applied by Corvite itself (sliders on the
/// same page); everything else is entered by the user in the driver program.
/// </summary>
public static class GpuGuide
{
    private const string Minus = "−";

    public static Guide For(GpuEntry g, Func<string, string> t)
    {
        var mhz = t("mhz");
        var rows = new List<GuideRow>();
        var notes = new List<string>();
        string where;
        string[] steps;

        if (g.IsNvidia)
        {
            where = "gg.w.nv";
            steps = new[] { "gg.s.nv1", "gg.s.nv2", "gg.s.nv3", "gg.s.nv4", "gg.s.nv5", "gg.s.nv6", "gg.s.nv7" };
            rows.Add(new GuideRow("gg.p.pow", "100 %", "105 %", "110 %"));

            var (c1, c2, c3) = g.S switch
            {
                "nv10" => (50, 100, 150),
                "nv40" => (50, 120, 180),
                "nv50" => (75, 150, 200),
                _ => (30, 75, 120)           // GTX 16, RTX 20, RTX 30
            };
            rows.Add(new GuideRow("gg.p.core", $"+{c1} {mhz}", $"+{c2} {mhz}", $"+{c3} {mhz}"));

            var (m1, m2, m3) = g.Mem switch
            {
                "GDDR5" => (200, 400, 600),
                "GDDR5X" => (250, 500, 750),
                "GDDR6X" => (200, 400, 600),
                "GDDR7" => (200, 400, 600),
                _ => (250, 500, 800)         // GDDR6
            };
            rows.Add(new GuideRow("gg.p.mem", $"+{m1} {mhz}", $"+{m2} {mhz}", $"+{m3} {mhz}"));

            rows.Add(new GuideRow("gg.p.temp", "80 °C", "83 °C", "87 °C"));
            rows.Add(new GuideRow("gg.p.fan", t("gg.v.auto"), "70 °C → 60 %", "75 °C → 80 %"));
            rows.Add(new GuideRow("gg.p.pm", t("gg.v.opt"), t("gg.v.maxperf"), t("gg.v.maxperf")));
            rows.Add(new GuideRow("gg.p.ll", t("gg.v.off"), t("gg.v.on"), "Ultra"));
            rows.Add(new GuideRow("gg.p.sc", t("gg.v.def"), $"10 {t("gg.gb")}", $"100 {t("gg.gb")}"));

            notes.Add("gg.n.nv");
            if (g.Mem == "GDDR6X") notes.Add("gg.n.g6x");
            notes.Add("gg.n.reflex");
        }
        else
        {
            where = "gg.w.amd";
            steps = new[] { "gg.s.amd1", "gg.s.amd2", "gg.s.amd3", "gg.s.amd4", "gg.s.amd5", "gg.s.amd6" };
            var old = g.S is "amdgcn" or "amd5";
            rows.Add(new GuideRow("gg.p.pow", "0 %", old ? "+10 %" : "+5 %", old ? "+20 %" : "+15 %"));

            var (v1, v2, v3) = g.S is "amd7" or "amd9" ? (20, 40, 60) : (25, 50, 75);
            rows.Add(new GuideRow("gg.p.volt", $"{Minus}{v1} mV", $"{Minus}{v2} mV", $"{Minus}{v3} mV"));
            rows.Add(new GuideRow("gg.p.coreA", t("gg.v.stock"), $"+50 {mhz}", $"+100 {mhz}"));
            rows.Add(new GuideRow("gg.p.mem", t("gg.v.stock"), $"+50 {mhz}", g.Mem == "HBM2" ? $"+100 {mhz}" : $"+75 {mhz}"));
            if (g.S is "amd6" or "amd7" or "amd9")
                rows.Add(new GuideRow("gg.p.ft", t("gg.v.def"), "Fast Timing", "Fast Timing"));
            rows.Add(new GuideRow("gg.p.fan", t("gg.v.auto"), "70 °C → 60 %", "75 °C → 80 %"));
            rows.Add(new GuideRow("gg.p.al", t("gg.v.off"), t("gg.v.on"), t("gg.v.on")));
            rows.Add(new GuideRow("gg.p.scA", t("gg.v.amdopt"), t("gg.v.always"), t("gg.v.always")));
            if (g.S != "amdgcn")
                rows.Add(new GuideRow("gg.p.sam", "Auto", t("gg.v.on"), t("gg.v.on")));

            notes.Add("gg.n.amd");
        }

        notes.Add("cg.n.how");
        notes.Add("gg.n.warn");
        return new Guide(rows, notes, where, steps);
    }
}
