using System.Text.Json;
using System.Text.RegularExpressions;
using Corvite.Core.Catalog;

namespace Corvite.Core.Hardware;

/// <summary>One reference row: level 0 = mild, 1 = medium, 2 = strong overclock.</summary>
public sealed record RamRow(int Level, int Speed, string Timings, string Dram, string Ctrl, bool WithinKit);

/// <summary>
/// Reference data for the "RAM timings" tab (TZ 9). Nothing here is applied to the machine.
/// Rows come from Data/ram_timings.json (type -> CPU vendor -> 3 rows). Chip hints are keyed by the module maker reported by
/// the system; the actual die (e.g. Samsung B-die) cannot be read from Windows, so hints stay general.
/// </summary>
public static class RamTimings
{
    private sealed class RowDto { public int Speed { get; set; } public string Timings { get; set; } = ""; public string Dram { get; set; } = ""; public string Ctrl { get; set; } = ""; }

    private static Dictionary<string, Dictionary<string, List<RowDto>>>? _data;

    private static Dictionary<string, Dictionary<string, List<RowDto>>> Data()
    {
        if (_data != null) return _data;
        using var s = typeof(RamTimings).Assembly.GetManifestResourceStream("Corvite.Core.Data.ram_timings.json");
        _data = s == null ? new() : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<RowDto>>>>(s,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return _data;
    }

    /// <summary>"DDR4" / "DDR5" for DDR4, DDR5 and their LP variants; null when there is no reference data (DDR3, unknown).</summary>
    public static string? NormalizeType(string type)
    {
        if (type.Contains("DDR5", StringComparison.OrdinalIgnoreCase)) return "DDR5";
        if (type.Contains("DDR4", StringComparison.OrdinalIgnoreCase)) return "DDR4";
        return null;
    }

    public static IReadOnlyList<RamRow> For(RamInfo ram, CpuVendor cpu)
    {
        var ty = NormalizeType(ram.Type);
        if (ty == null || !Data().TryGetValue(ty, out var byVendor)) return Array.Empty<RamRow>();
        if (!byVendor.TryGetValue(cpu == CpuVendor.Amd ? "amd" : "intel", out var rows)) return Array.Empty<RamRow>();
        return rows.Select((r, i) => new RamRow(i, r.Speed, r.Timings, r.Dram, r.Ctrl, r.Speed <= ram.SpeedMts)).ToList();
    }

    /// <summary>BIOS path by board maker (TZ 9). Returns null for laptops: the UI shows the localized "usually not available" text.</summary>
    public static string? BiosPath(string board, bool laptop)
    {
        if (laptop) return null;
        if (Regex.IsMatch(board, "ASUS", RegexOptions.IgnoreCase)) return "Ai Tweaker → DRAM Frequency";
        if (Regex.IsMatch(board, "MSI", RegexOptions.IgnoreCase)) return "OC → DRAM Frequency";
        if (Regex.IsMatch(board, "GIGABYTE", RegexOptions.IgnoreCase)) return "Tweaker → Extreme Memory Profile";
        return "OC Tweaker → DRAM Configuration";
    }

    /// <summary>Resource key of the chip hint for this module maker and memory type.</summary>
    public static string ChipNoteKey(RamInfo ram)
    {
        var ddr5 = NormalizeType(ram.Type) == "DDR5";
        var v = ram.Vendor;
        if (Regex.IsMatch(v, "samsung", RegexOptions.IgnoreCase)) return ddr5 ? "r_chip_sam5" : "r_chip_sam4";
        if (Regex.IsMatch(v, "hynix", RegexOptions.IgnoreCase)) return ddr5 ? "r_chip_hyn5" : "r_chip_hyn4";
        if (Regex.IsMatch(v, "micron|crucial", RegexOptions.IgnoreCase)) return ddr5 ? "r_chip_mic5" : "r_chip_mic4";
        return "r_chip_gen";
    }
}
