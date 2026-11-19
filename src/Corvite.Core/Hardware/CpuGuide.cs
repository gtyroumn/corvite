namespace Corvite.Core.Hardware;

/// <summary>One line of the tuning table. ParamKey is a Strings.resx key; the three values are ready-to-show text.</summary>
public sealed record GuideRow(string ParamKey, string Safe, string Game, string Max);

public sealed record Guide(IReadOnlyList<GuideRow> Rows, IReadOnlyList<string> NoteKeys, string WhereKey);

/// <summary>
/// Reference values for tuning a processor in BIOS or the manufacturer's program. Rules, not measurements: they start from the
/// published defaults of the chosen model and step away from them in small, commonly stable increments.
/// Corvite does not write any of these values itself (that would need a kernel driver, which anti-cheat software may block).
/// </summary>
public static class CpuGuide
{
    private const string Minus = "−";

    private static int R5(double v) => (int)(Math.Round(v / 5.0) * 5);

    public static Guide For(CpuEntry c, Func<string, string> t)
    {
        var w = t("w"); var mhz = t("mhz"); var na = t("cg.v.na");
        var rows = new List<GuideRow>();
        var notes = new List<string>();
        var bal = t("cg.v.bal"); var high = t("cg.v.high");
        string where;

        rows.Add(new GuideRow("cg.p.pwr", bal, high, high));

        if (c.IsLaptop)
        {
            where = "cg.w.lap";
            rows.Add(new GuideRow("cg.p.tlim", "80 °C", "85 °C", c.J + " °C"));
            if (!c.IsAmd && c.Q <= 11)
                rows.Add(new GuideRow("cg.p.uv", $"{Minus}0.020 V", $"{Minus}0.050 V", $"{Minus}0.080 V"));
            notes.Add("cg.n.lap");
        }
        else if (c.IsAmd)
        {
            where = c.Q <= 2 ? "cg.w.amdold" : "cg.w.amd";
            if (c.Q <= 2)
            {
                rows.Add(new GuideRow("cg.p.tlim", "80 °C", "85 °C", c.J + " °C"));
                notes.Add("cg.n.amdold");
            }
            else
            {
                if (c.X3d && c.Q == 4)
                {
                    rows.Add(new GuideRow("cg.p.pbo", na, na, na));
                    rows.Add(new GuideRow("cg.p.ppt", na, na, na));
                }
                else
                {
                    rows.Add(new GuideRow("cg.p.pbo", "Auto", "Enabled", "Enabled"));
                    rows.Add(new GuideRow("cg.p.ppt", $"{c.P} {w}", $"{R5(c.P * 1.1)} {w}", $"{R5(c.P * 1.25)} {w}"));
                }

                if (c.Q >= 4)
                    rows.Add(new GuideRow("cg.p.co", $"{Minus}5", $"{Minus}15", c.Q >= 5 ? $"{Minus}30" : $"{Minus}25"));

                if (c.X3d && c.Q == 4) rows.Add(new GuideRow("cg.p.boost", na, na, na));
                else if (c.Q >= 5) rows.Add(new GuideRow("cg.p.boost", "0", $"+100 {mhz}", $"+200 {mhz}"));
                else if (c.Q == 4) rows.Add(new GuideRow("cg.p.boost", "0", $"+50 {mhz}", $"+100 {mhz}"));
                else rows.Add(new GuideRow("cg.p.boost", "0", $"+25 {mhz}", $"+50 {mhz}"));

                rows.Add(new GuideRow("cg.p.tlim", "80 °C", "85 °C", c.J + " °C"));
                if (c.Q == 3) notes.Add("cg.n.zen2");
                if (c.X3d) notes.Add("cg.n.x3d");
            }
        }
        else
        {
            where = "cg.w.intel";
            var pl2 = c.P > 0 ? c.P : R5(c.D * 1.25);
            rows.Add(new GuideRow("cg.p.pl",
                $"PL1 {c.D} / PL2 {Math.Max(c.D, R5(pl2 * 0.75))} {w}",
                $"PL1 {c.D} / PL2 {pl2} {w}",
                $"PL1 = PL2 = {pl2} {w}"));
            if (c.Unlocked)
            {
                var (a, b, m) = c.Q >= 13 ? ("0.020", "0.040", "0.060") : ("0.020", "0.050", "0.080");
                rows.Add(new GuideRow("cg.p.uv", $"{Minus}{a} V", $"{Minus}{b} V", $"{Minus}{m} V"));
            }
            else
            {
                rows.Add(new GuideRow("cg.p.uv", na, na, na));
                notes.Add("cg.n.locked");
            }
            rows.Add(new GuideRow("cg.p.tcc", $"15 (≈{c.J - 15} °C)", $"10 (≈{c.J - 10} °C)", $"0 ({c.J} °C)"));
            if (c.Q is 13 or 14) notes.Add("cg.n.i1314");
        }

        notes.Add("cg.n.how");
        notes.Add("cg.n.warn");
        return new Guide(rows, notes, where);
    }
}
