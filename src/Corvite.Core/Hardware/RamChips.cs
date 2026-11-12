using System.Text.Json;
using System.Text.RegularExpressions;
using Corvite.Core.Catalog;

namespace Corvite.Core.Hardware;

public sealed record RamChipRow(int Speed, string? Timings, string Dram);

/// <summary>One memory die (chip revision) with three overclock rows. Confidence: high / medium / low (little data in the sources).</summary>
public sealed record RamChip(string Id, string Type, string Maker, string Name, string Confidence, IReadOnlyList<RamChipRow> Rows)
{
    public string NoteKey => "chip." + Id;
}

/// <summary>
/// Chip database for the RAM tab (Data/ram_chips.json, collected from public overclocker guides) and the part-number hint.
/// The hint is only a suggestion: it reads the die letter from known manufacturer naming schemes and the user confirms it.
/// </summary>
public static class RamChips
{
    private sealed class RowDto { public int Speed { get; set; } public string? Timings { get; set; } public string Dram { get; set; } = ""; }
    private sealed class ChipDto { public string Id { get; set; } = ""; public string Type { get; set; } = ""; public string Maker { get; set; } = ""; public string Name { get; set; } = ""; public string Conf { get; set; } = "medium"; public List<RowDto> Rows { get; set; } = new(); }
    private sealed class FileDto { public List<ChipDto> Chips { get; set; } = new(); }

    private static IReadOnlyList<RamChip>? _all;

    public static IReadOnlyList<RamChip> All
    {
        get
        {
            if (_all != null) return _all;
            using var s = typeof(RamChips).Assembly.GetManifestResourceStream("Corvite.Core.Data.ram_chips.json");
            var f = s == null ? new FileDto() : JsonSerializer.Deserialize<FileDto>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new FileDto();
            _all = f.Chips.Select(c => new RamChip(c.Id, c.Type, c.Maker, c.Name, c.Conf,
                c.Rows.Select(r => new RamChipRow(r.Speed, r.Timings, r.Dram)).ToList())).ToList();
            return _all;
        }
    }

    public static RamChip? Find(string? id) => id == null ? null : All.FirstOrDefault(c => c.Id == id);

    public static IReadOnlyList<RamChip> ForType(string? type) => type == null ? Array.Empty<RamChip>() : All.Where(c => c.Type == type).ToList();

    /// <summary>
    /// Best guess of the die from the module part number (DDR4 only):
    /// Samsung M471A1K43CB1-CTD: the letter after the 4-character density/organisation code is the die (B, C, D, E);
    /// SK hynix HMA81GS6CJR8N-VK: the die name is part of the number (AFR, CJR, DJR, MFR);
    /// Micron 4ATF51264HZ-3G2J1: the letter before the last digit of the speed suffix is the revision (E, B, J, R).
    /// Returns null whenever the scheme is not recognised, so the app never pretends to know.
    /// </summary>
    public static RamChip? DetectFromPartNumber(RamInfo ram)
    {
        if (RamTimings.NormalizeType(ram.Type) != "DDR4") return null;
        var pn = ram.PartNumber.Trim().ToUpperInvariant();
        if (pn.Length < 8) return null;

        var sam = Regex.Match(pn, @"^M[0-9]{3}[A-Z][0-9A-Z]{4}([A-Z])");
        if (sam.Success)
            return sam.Groups[1].Value switch
            {
                "B" => Find("ddr4-sam-b"), "C" => Find("ddr4-sam-c"), "D" => Find("ddr4-sam-d"), "E" => Find("ddr4-sam-e"), _ => null
            };

        var hyn = Regex.Match(pn, "(AFR|CJR|DJR|MFR)");
        if (pn.StartsWith("HM") && hyn.Success)
            return hyn.Groups[1].Value switch
            {
                "AFR" => Find("ddr4-hyn-afr"), "CJR" => Find("ddr4-hyn-cjr"), "DJR" => Find("ddr4-hyn-djr"), _ => Find("ddr4-hyn-mfr")
            };

        var mic = Regex.Match(pn, @"ATF[0-9A-Z]+-[0-9][A-Z][0-9]([A-Z])[0-9]$");
        if (mic.Success)
            return mic.Groups[1].Value switch
            {
                "E" => Find("ddr4-mic-e"), "B" => Find("ddr4-mic-b8"), "J" => Find("ddr4-mic-j"), "R" => Find("ddr4-mic-r"), _ => null
            };

        return null;
    }
}
