namespace Corvite.Core.Ai;

/// <summary>What the diagnosis looks at. Live values are null when sensors are not available.</summary>
public sealed record DiagInput(
    double? CpuTemp, double? CpuLoad, double? GpuTemp, double? GpuLoad,
    double RamUsedGb, double RamTotalGb,
    string RamType, string RamProfile,
    bool HasDisk, bool DiskIsSsd,
    bool Laptop, bool OnBattery, int Tier);

/// <summary>One finding: a stable id (the app maps it to text), severity 1 note / 2 warning / 3 serious, and numbers for the text.</summary>
public sealed record Finding(string Id, int Severity, double Value = 0);

/// <summary>
/// "Why does it lag": rule-based look at the current state of the machine. Pure logic, no texts.
/// Static findings do not depend on sensors (used for the hints on the Overview); live findings do.
/// </summary>
public static class Diagnosis
{
    public static List<Finding> Run(DiagInput d, bool includeLive = true)
    {
        var r = new List<Finding>();

        if (includeLive)
        {
            if (d.CpuTemp is double ct && ct >= 85) r.Add(new Finding("cpu_hot", ct >= 92 ? 3 : 2, Math.Round(ct)));
            if (d.GpuTemp is double gt && gt >= 83) r.Add(new Finding("gpu_hot", gt >= 90 ? 3 : 2, Math.Round(gt)));
            if (d.RamTotalGb > 0 && d.RamUsedGb / d.RamTotalGb >= 0.85)
                r.Add(new Finding("ram_full", d.RamUsedGb / d.RamTotalGb >= 0.93 ? 3 : 2, Math.Round(d.RamUsedGb / d.RamTotalGb * 100)));
            if (d.CpuLoad is double cl && d.GpuLoad is double gl && cl >= 90 && gl < 60) r.Add(new Finding("cpu_bound", 1, Math.Round(cl)));
        }

        if (d.Laptop && d.OnBattery) r.Add(new Finding("battery", 2));
        if (d.HasDisk && !d.DiskIsSsd) r.Add(new Finding("hdd", 2));
        if (string.Equals(d.RamProfile, "JEDEC", StringComparison.OrdinalIgnoreCase) &&
            (d.RamType.StartsWith("DDR4", StringComparison.OrdinalIgnoreCase) || d.RamType.StartsWith("DDR5", StringComparison.OrdinalIgnoreCase)))
            r.Add(new Finding("ram_slow", 1));
        if (d.Tier < 2) r.Add(new Finding("tier_low", 1));

        return r.OrderByDescending(f => f.Severity).ToList();
    }
}
