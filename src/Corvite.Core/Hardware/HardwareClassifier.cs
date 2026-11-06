using Corvite.Core.Catalog;

namespace Corvite.Core.Hardware;

/// <summary>Rough performance class 0..4 used by the profile advisor (tier >= 2 → "Gaming" profile).</summary>
public static class HardwareClassifier
{
    public static int Tier(int logicalThreads, GpuVendor gpu, double ramGb)
    {
        var cpu = logicalThreads >= 20 ? 3 : logicalThreads >= 12 ? 2 : logicalThreads >= 8 ? 1 : 0;
        var g = gpu == GpuVendor.Nvidia || gpu == GpuVendor.Amd ? 1 : 0;
        var ram = ramGb > 0 && ramGb < 12 ? -1 : 0;
        return Math.Clamp(cpu + g + ram, 0, 4);
    }

    /// <summary>"12 GB" / "128 MB" with caller-supplied (localised) units.</summary>
    public static string FormatMemory(int mb, string gbUnit, string mbUnit) =>
        mb >= 1024 ? $"{Math.Round(mb / 1024.0)} {gbUnit}" : $"{mb} {mbUnit}";
}
