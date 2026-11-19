using System.Text.Json;
using System.Text.RegularExpressions;

namespace Corvite.Core.Hardware;

/// <summary>One reference processor (Data/cpus.json). Reference data only: nothing here is written to the machine.</summary>
public sealed class CpuEntry
{
    public string N { get; set; } = "";      // display name
    public string M { get; set; } = "";      // model token used to recognise the installed CPU
    public string V { get; set; } = "";      // "amd" | "intel"
    public string S { get; set; } = "";      // socket id, or "Mobile AMD" / "Mobile Intel"
    public int L { get; set; }               // 1 = laptop
    public string R { get; set; } = "";      // architecture name
    public int Q { get; set; }               // AMD: 1 Zen, 2 Zen+, 3 Zen 2, 4 Zen 3, 5 Zen 4, 6 Zen 5; Intel: generation (15 = Core Ultra 200)
    public int C { get; set; }               // cores
    public int T { get; set; }               // threads
    public double B { get; set; }            // base GHz
    public double X { get; set; }            // boost GHz
    public int D { get; set; }               // TDP / PL1, W
    public int P { get; set; }               // AMD desktop: default PPT; Intel desktop: maximum turbo power (PL2); 0 = unknown
    public int J { get; set; }               // Tjmax, °C
    public string F { get; set; } = "";      // flags: "k" unlocked multiplier, "3" 3D V-Cache

    public string Id => (V + "-" + S + "-" + M).ToLowerInvariant().Replace(' ', '-');
    public bool IsLaptop => L == 1;
    public bool IsAmd => V == "amd";
    public bool Unlocked => F.Contains('k');
    public bool X3d => F.Contains('3');
}

/// <summary>Processor list for the tuning guide: Ryzen from the first generation, Intel from the 9th generation on.</summary>
public static class CpuDb
{
    private static List<CpuEntry>? _all;

    public static IReadOnlyList<CpuEntry> All
    {
        get
        {
            if (_all != null) return _all;
            try
            {
                using var s = typeof(CpuDb).Assembly.GetManifestResourceStream("Corvite.Core.Data.cpus.json");
                _all = s == null ? new() : JsonSerializer.Deserialize<List<CpuEntry>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { _all = new(); }
            return _all;
        }
    }

    /// <summary>Sockets offered for the device type: desktop sockets for a PC, the two mobile groups for a laptop.</summary>
    public static IReadOnlyList<string> Sockets(bool laptop) => laptop
        ? new[] { "Mobile AMD", "Mobile Intel" }
        : new[] { "AM4", "AM5", "LGA1151", "LGA1200", "LGA1700", "LGA1851" };

    /// <summary>Processors of one socket, newest generation first. Laptop processors never appear for a PC and the other way round.</summary>
    public static IReadOnlyList<CpuEntry> ForSocket(string socket, bool laptop) =>
        All.Where(c => c.S == socket && c.IsLaptop == laptop).OrderByDescending(c => c.Q).ThenBy(c => c.N, StringComparer.Ordinal).ToList();

    public static CpuEntry? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>Recognises the installed processor by its model number ("5800X", "i7-12700K"); the longest matching model wins (5800X3D before 5800X).</summary>
    public static CpuEntry? Detect(string cpuName, bool laptop)
    {
        if (string.IsNullOrWhiteSpace(cpuName)) return null;
        var amd = cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        var intel = cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        CpuEntry? best = null;
        foreach (var c in All)
        {
            if (c.IsLaptop != laptop || (amd && !c.IsAmd) || (intel && c.IsAmd)) continue;
            if (!Regex.IsMatch(cpuName, @"(?<![A-Za-z0-9])" + Regex.Escape(c.M) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase)) continue;
            if (best == null || c.M.Length > best.M.Length) best = c;
        }
        return best;
    }
}
