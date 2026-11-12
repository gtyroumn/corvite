using Corvite.Core.Catalog;

namespace Corvite.Core.Hardware;

/// <summary>Installed memory. Type: DDR4 / DDR5 / LPDDR5 ...; Profile: JEDEC / XMP / EXPO or empty when unknown.</summary>
public sealed record RamInfo(string Type, double Gb, int Modules, int SpeedMts, string Vendor, string Profile, string PartNumber = "");

/// <summary>
/// Everything the UI needs to know about the machine. Unknown numbers are 0, unknown strings are empty
/// (the UI shows an em dash). Produced by Corvite.Hardware.HardwareDetector or by the demo configurations.
/// </summary>
public sealed record HardwareInfo(
    string Id,
    string CpuName, CpuVendor CpuBrand, string Cores, int Threads, int BaseMhz, int BoostMhz, string Cache, string Socket, int TdpW,
    string GpuName, GpuVendor GpuBrand, string Vram, int VramMb, int GpuCoreMhz, int GpuMemMhz,
    RamInfo Ram, string Board, string Disk, bool DiskIsSsd,
    bool IsLaptop, int Tier, bool IsDemo, string DeviceHint = "")
{
    public HardwareProfile ToProfile() => new(CpuBrand, GpuBrand, Tier);

    /// <summary>GPU tweaks are only offered for NVIDIA and (discrete) AMD cards.</summary>
    public bool GpuSupported => GpuBrand == GpuVendor.Nvidia || GpuBrand == GpuVendor.Amd;

    /// <summary>Used before detection finishes.</summary>
    public static HardwareInfo Placeholder { get; } = new(
        "placeholder", "", CpuVendor.Amd, "", 0, 0, 0, "", "", 0,
        "", GpuVendor.Nvidia, "", 0, 0, 0,
        new RamInfo("", 0, 0, 0, "", ""), "", "", false,
        false, 2, false);
}
