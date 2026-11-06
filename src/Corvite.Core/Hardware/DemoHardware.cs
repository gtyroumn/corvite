using System.Text.Json;
using Corvite.Core.Catalog;

namespace Corvite.Core.Hardware;

/// <summary>The six demo configurations of the prototype (debug only, started with --demo).</summary>
public static class DemoHardware
{
    private sealed class RamDto { public string Ty { get; set; } = ""; public double Gb { get; set; } public int St { get; set; } public int Sp { get; set; } public string Ven { get; set; } = ""; public string Pf { get; set; } = ""; }

    private sealed class Dto
    {
        public string Id { get; set; } = "";
        public int Lap { get; set; }
        public string Cpu { get; set; } = "";
        public string Cv { get; set; } = "";
        public string Core { get; set; } = "";
        public int Base { get; set; }
        public int Boost { get; set; }
        public string Cache { get; set; } = "";
        public string Sock { get; set; } = "";
        public int Tdp { get; set; }
        public string Gpu { get; set; } = "";
        public string Gv { get; set; } = "";
        public string Vram { get; set; } = "";
        public int Gc { get; set; }
        public int Gm { get; set; }
        public RamDto Ram { get; set; } = new();
        public string Mb { get; set; } = "";
        public int Tier { get; set; }
        public string Disk { get; set; } = "";
    }

    public static IReadOnlyList<HardwareInfo> Load()
    {
        var asm = typeof(DemoHardware).Assembly;
        using var s = asm.GetManifestResourceStream("Corvite.Core.Data.demo_hardware.json");
        if (s == null) return Array.Empty<HardwareInfo>();
        var items = JsonSerializer.Deserialize<List<Dto>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return items.Select(d => new HardwareInfo(
            d.Id,
            d.Cpu, d.Cv == "amd" ? CpuVendor.Amd : CpuVendor.Intel, d.Core, 0, d.Base, d.Boost, d.Cache, d.Sock, d.Tdp,
            d.Gpu, d.Gv switch { "nv" => GpuVendor.Nvidia, "rx" => GpuVendor.Amd, _ => GpuVendor.IntelArc }, d.Vram, 0, d.Gc, d.Gm,
            new RamInfo(d.Ram.Ty, d.Ram.Gb, d.Ram.St, d.Ram.Sp, d.Ram.Ven, d.Ram.Pf),
            d.Mb, d.Disk, d.Disk.Contains("SSD") || d.Disk.Contains("NVMe"),
            d.Lap == 1, d.Tier, true)).ToList();
    }
}
