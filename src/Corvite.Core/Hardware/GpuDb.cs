using System.Text.Json;
using System.Text.RegularExpressions;

namespace Corvite.Core.Hardware;

/// <summary>One reference desktop graphics card (Data/gpus.json). Reference data only: nothing here is written to the machine.</summary>
public sealed class GpuEntry
{
    public string N { get; set; } = "";      // display name
    public string M { get; set; } = "";      // model token used to recognise the installed card
    public string V { get; set; } = "";      // "nvidia" | "amd"
    public string S { get; set; } = "";      // series id: nv10 nv16 nv20 nv30 nv40 nv50 amdgcn amd5 amd6 amd7 amd9
    public string R { get; set; } = "";      // architecture
    public int Vram { get; set; }            // GB
    public string Mem { get; set; } = "";    // GDDR5 / GDDR5X / GDDR6 / GDDR6X / GDDR7 / HBM2
    public int D { get; set; }               // reference board power, W
    public int X { get; set; }               // reference boost clock, MHz

    public string Id => (V + "-" + M).ToLowerInvariant().Replace(' ', '-');
    public bool IsNvidia => V == "nvidia";
}

/// <summary>Graphics card list for the tuning guide: GeForce GTX 1060 and newer, Radeon cards of the same performance class and age.</summary>
public static class GpuDb
{
    private static List<GpuEntry>? _all;

    public static IReadOnlyList<GpuEntry> All
    {
        get
        {
            if (_all != null) return _all;
            try
            {
                using var s = typeof(GpuDb).Assembly.GetManifestResourceStream("Corvite.Core.Data.gpus.json");
                _all = s == null ? new() : JsonSerializer.Deserialize<List<GpuEntry>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { _all = new(); }
            return _all;
        }
    }

    /// <summary>Series in display order (NVIDIA oldest to newest, then AMD).</summary>
    public static IReadOnlyList<string> Series() => All.Select(g => g.S).Distinct().ToList();

    public static IReadOnlyList<GpuEntry> ForSeries(string series) => All.Where(g => g.S == series).ToList();

    public static GpuEntry? Find(string id) => All.FirstOrDefault(g => g.Id == id);

    /// <summary>Recognises the installed card by its model ("GTX 1070", "RX 6700 XT"); the longest matching model wins (1070 Ti before 1070).</summary>
    public static GpuEntry? Detect(string gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        var nv = gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase);
        var amd = gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        GpuEntry? best = null;
        foreach (var g in All)
        {
            if ((nv && !g.IsNvidia) || (amd && g.IsNvidia)) continue;
            var pat = Regex.Escape(g.M).Replace("\\ ", @"\s*");
            if (!Regex.IsMatch(gpuName, @"(?<![A-Za-z0-9])" + pat + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase)) continue;
            if (best == null || g.M.Length > best.M.Length) best = g;
        }
        return best;
    }
}
