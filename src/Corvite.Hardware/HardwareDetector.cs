using System.Management;
using System.Text.RegularExpressions;
using Corvite.Core.Catalog;
using Corvite.Core.Hardware;

namespace Corvite.Hardware;

/// <summary>
/// One-shot hardware detection through WMI/CIM (TZ 4.2, 7.4). Every query is independent and guarded:
/// a failing query leaves the corresponding fields at their "unknown" defaults, it never throws.
/// </summary>
public static class HardwareDetector
{
    private static readonly int[] LaptopChassis = { 8, 9, 10, 11, 14, 30, 31, 32 };
    private static readonly int[] DesktopChassis = { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 35, 36 };

    public static HardwareInfo Detect()
    {
        // ---- CPU
        var cpuObj = Q("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor").FirstOrDefault();
        var cpuRaw = cpuObj != null ? S(cpuObj, "Name") : "";
        var cpuName = CleanCpu(cpuRaw);
        var cpuBrand = (cpuObj != null && S(cpuObj, "Manufacturer").Contains("AMD", StringComparison.OrdinalIgnoreCase))
                       || cpuRaw.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            ? CpuVendor.Amd : CpuVendor.Intel;
        var cores = cpuObj != null ? (int)N(cpuObj, "NumberOfCores") : 0;
        var threads = cpuObj != null ? (int)N(cpuObj, "NumberOfLogicalProcessors") : 0;
        var baseMhz = cpuObj != null ? (int)N(cpuObj, "MaxClockSpeed") : 0; // WMI "MaxClockSpeed" is the rated base clock
        var l2 = cpuObj != null ? N(cpuObj, "L2CacheSize") : 0;
        var l3 = cpuObj != null ? N(cpuObj, "L3CacheSize") : 0;
        var cache = l3 > 0 ? $"{l3 / 1024} MB" : l2 > 0 ? $"{l2 / 1024} MB" : "";
        var socket = cpuObj != null ? S(cpuObj, "SocketDesignation") : "";

        // ---- GPU (best candidate: NVIDIA > discrete AMD > Intel > other)
        var gpus = Q("SELECT Name, AdapterRAM FROM Win32_VideoController")
            .Select(o => (Name: Clean(S(o, "Name")), Ram: N(o, "AdapterRAM")))
            .Where(g => g.Name.Length > 0 && !Regex.IsMatch(g.Name, "Microsoft Basic|Remote|Virtual|Parsec|Indirect|Meta Virtual", RegexOptions.IgnoreCase))
            .OrderByDescending(g => GpuScore(g.Name)).ThenByDescending(g => g.Ram)
            .ToList();
        var gpuName = gpus.Count > 0 ? gpus[0].Name : "";
        var gpuBrand = ClassifyGpu(gpuName);
        var vramMb = gpus.Count > 0 ? (int)(gpus[0].Ram / (1024 * 1024)) : 0; // uint32 in WMI: capped at 4 GB, refined by sensors later

        // ---- RAM
        var ram = DetectRam(cpuBrand);

        // ---- device type, board, disk
        var (isLaptop, deviceHint) = DetectDevice(cpuRaw);
        var board = DetectBoard(isLaptop);
        var (disk, ssd) = DetectDisk();

        var tier = HardwareClassifier.Tier(threads, gpuBrand, ram.Gb);
        var coresText = cores > 0 ? $"{cores} / {threads}" : "";

        return new HardwareInfo(
            "detected",
            cpuName, cpuBrand, coresText, threads, baseMhz, 0, cache, socket, 0,
            gpuName, gpuBrand, "", vramMb, 0, 0,
            ram, board, disk, ssd,
            isLaptop, tier, false, deviceHint);
    }

    // ------------------------------------------------------------------ GPU
    private static int GpuScore(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return 4;
        if (ClassifyGpu(name) == GpuVendor.Amd) return 3;
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase) && name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    /// <summary>AMD "Radeon Graphics" integrated parts are NOT treated as supported discrete cards.</summary>
    public static GpuVendor ClassifyGpu(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.IntelArc;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(name, @"\bRX\b|Radeon Pro|Radeon VII|Vega (56|64)", RegexOptions.IgnoreCase) ? GpuVendor.Amd : GpuVendor.Unknown;
        return GpuVendor.Unknown;
    }

    // ------------------------------------------------------------------ RAM
    private static RamInfo DetectRam(CpuVendor cpu)
    {
        var mods = Q("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, PartNumber, FormFactor FROM Win32_PhysicalMemory");
        if (mods.Count == 0) return new RamInfo("", 0, 0, 0, "", "");

        var totalGb = Math.Round(mods.Sum(m => (double)N(m, "Capacity")) / 1073741824.0, 1);
        var configured = mods.Max(m => N(m, "ConfiguredClockSpeed"));
        var rated = mods.Max(m => N(m, "Speed"));
        var speed = (int)(configured > 0 ? configured : rated);
        var first = mods[0];

        var type = (int)N(first, "SMBIOSMemoryType") switch
        {
            24 => "DDR3",
            26 => "DDR4",
            27 or 28 or 29 => "LPDDR3",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => speed >= 4400 ? "DDR5" : speed >= 1600 ? "DDR4" : ""
        };

        var sodimm = N(first, "FormFactor") == 12;
        var profile = "";
        if (type == "DDR4" || type == "DDR5")
        {
            if (sodimm) profile = "JEDEC";
            else
            {
                var jedecMax = type == "DDR5" ? 5600 : 2933;
                profile = speed > jedecMax ? (cpu == CpuVendor.Amd && type == "DDR5" ? "EXPO" : "XMP") : "JEDEC";
            }
        }

        return new RamInfo(type, totalGb, mods.Count, speed, RamVendor(S(first, "Manufacturer"), S(first, "PartNumber")), profile);
    }

    private static string RamVendor(string manufacturer, string part)
    {
        var m = manufacturer.Trim();
        if (m.Length >= 4 && Regex.IsMatch(m, "^[0-9A-Fa-f]+$"))
        {
            var id = m[..4].ToUpperInvariant();
            var known = id switch
            {
                "802C" => "Micron", "80AD" => "SK hynix", "80CE" => "Samsung", "859B" => "Crucial",
                "0198" => "Kingston", "029E" => "Corsair", "04CD" => "G.Skill", _ => ""
            };
            m = known;
        }
        if (m.Length == 0 || m.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || m.Equals("Undefined", StringComparison.OrdinalIgnoreCase)) m = "";
        part = part.Trim();
        return m.Length > 0 ? m : part;
    }

    // ------------------------------------------------------------------ device type / board / disk
    private const RegexOptions Ic = RegexOptions.IgnoreCase;

    private static readonly Regex DesktopModel = new(
        @"Desktop|Tower|ThinkCentre|IdeaCentre|OptiPlex|All-in-One|\bAIO\b|Mini PC|\bNUC\b|Aurora|Orion|Legion T\d|ProDesk|EliteDesk|Aspire (TC|XC)|OMEN \d+L|Workstation", Ic);

    private static readonly Regex LaptopModel = new(
        @"Laptop|Notebook|Vivobook|Zenbook|ExpertBook|ThinkPad|IdeaPad|Legion|Yoga|Inspiron|XPS|Latitude|Aspire|Nitro|Swift|Predator Helios|Pavilion|Spectre|EliteBook|ProBook|Victus|OMEN|Galaxy Book|MacBook|Surface|LOQ|Katana|Zephyrus|ROG Flow|TUF Gaming [AF]\d|Modern \d|Prestige|Stealth \d|Blade", Ic);

    private static readonly Regex MobileCpu = new(
        @"\b\d{4,5}(U|P|H|HX|HK|HS|G[147])\b|Ultra \d+ \d{3}(U|H|V)\b|Ryzen [3579] \d{4}(U|HS|H|HX|HX3D)\b", Ic);

    private static readonly Regex DesktopCpu = new(
        @"\b\d{4,5}(K|KF|KS|F)\b|Ryzen [3579] \d{4}(X|X3D|XT)\b|Ultra \d+ \d{3}K", Ic);

    /// <summary>
    /// Scored guess (TZ 7.4): chassis type and system type (strong), battery, model name and CPU series (supporting).
    /// Hint lists the signals that spoke (chassis, battery, model, cpu); an empty hint means "no evidence either way",
    /// in which case the UI makes no recommendation.
    /// </summary>
    private static (bool Laptop, string Hint) DetectDevice(string cpuName)
    {
        var score = 0.0;
        var hints = new List<string>();
        void Vote(double w, string hint) { score += w; if (!hints.Contains(hint)) hints.Add(hint); }

        // chassis type (SMBIOS)
        var enc = Q("SELECT ChassisTypes FROM Win32_SystemEnclosure").FirstOrDefault();
        var types = new List<int>();
        try { if (enc?["ChassisTypes"] is Array arr) foreach (var v in arr) types.Add(Convert.ToInt32(v)); }
        catch { /* ignore */ }
        if (types.Any(t => LaptopChassis.Contains(t))) Vote(3, "chassis");
        else if (types.Any(t => DesktopChassis.Contains(t))) Vote(-3, "chassis");

        // system type + model name
        var cs = Q("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
        if (cs != null)
        {
            var kind = N(cs, "PCSystemType"); // 1 desktop, 2 mobile, 3 workstation, 8 slate
            if (kind == 2 || kind == 8) Vote(2, "chassis");
            else if (kind == 1 || kind == 3) Vote(-2, "chassis");

            var model = $"{S(cs, "Manufacturer")} {S(cs, "Model")}";
            if (DesktopModel.IsMatch(model)) Vote(-2, "model");
            else if (LaptopModel.IsMatch(model)) Vote(2, "model");
        }

        // battery
        if (Q("SELECT BatteryStatus FROM Win32_Battery").Count > 0) Vote(2, "battery");
        else score -= 1; // no battery: weak desktop evidence, never a hint on its own

        // CPU series
        if (MobileCpu.IsMatch(cpuName)) Vote(1.5, "cpu");
        else if (DesktopCpu.IsMatch(cpuName)) Vote(-1.5, "cpu");

        return (score > 0, hints.Count == 0 ? "" : string.Join(",", hints));
    }

    private static string DetectBoard(bool laptop)
    {
        if (laptop)
        {
            var cs = Q("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs != null) return Clean($"{Maker(S(cs, "Manufacturer"))} {S(cs, "Model")}");
        }
        var bb = Q("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        return bb != null ? Clean($"{Maker(S(bb, "Manufacturer"))} {S(bb, "Product")}") : "";
    }

    private static string Maker(string m)
    {
        if (m.Contains("ASUS", StringComparison.OrdinalIgnoreCase)) return "ASUS";
        if (m.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || m.Contains("MSI", StringComparison.OrdinalIgnoreCase)) return "MSI";
        if (m.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)) return "GIGABYTE";
        if (m.Contains("ASRock", StringComparison.OrdinalIgnoreCase)) return "ASRock";
        return Regex.Replace(m, @"\s*(Co\.,?\s*Ltd\.?|Inc\.?|Corporation|Corp\.?|COMPUTER)\b", "", RegexOptions.IgnoreCase).Trim(' ', ',', '.');
    }

    private static (string Label, bool Ssd) DetectDisk()
    {
        var pd = Q("SELECT DeviceId, MediaType, BusType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
            .OrderBy(o => int.TryParse(S(o, "DeviceId"), out var d) ? d : 99).FirstOrDefault();
        long size; string kind; bool ssd;
        if (pd != null)
        {
            var bus = N(pd, "BusType"); var media = N(pd, "MediaType");
            size = N(pd, "Size");
            ssd = bus == 17 || media == 4;
            kind = bus == 17 ? "NVMe" : media == 4 ? "SSD" : media == 3 ? "HDD" : "";
        }
        else
        {
            var dd = Q("SELECT Model, Size FROM Win32_DiskDrive").FirstOrDefault();
            if (dd == null) return ("", false);
            size = N(dd, "Size");
            var model = S(dd, "Model");
            ssd = model.Contains("SSD", StringComparison.OrdinalIgnoreCase) || model.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
            kind = ssd ? "SSD" : "";
        }
        var gb = size / 1_000_000_000.0;
        var sizeText = gb >= 950 ? $"{Math.Round(gb / 1000.0, 1)} TB" : gb > 0 ? $"{Math.Round(gb)} GB" : "";
        return ($"{kind} {sizeText}".Trim(), ssd);
    }

    // ------------------------------------------------------------------ helpers
    private static List<ManagementObject> Q(string wql, string scope = @"root\cimv2")
    {
        var list = new List<ManagementObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            foreach (ManagementObject o in searcher.Get()) list.Add(o);
        }
        catch { /* query not available on this machine */ }
        return list;
    }

    private static string S(ManagementBaseObject o, string p)
    {
        try { return (o[p]?.ToString() ?? "").Trim(); } catch { return ""; }
    }

    private static long N(ManagementBaseObject o, string p)
    {
        try { var v = o[p]; return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; }
    }

    private static string Clean(string s)
    {
        s = s.Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "").Replace("(tm)", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string CleanCpu(string s)
    {
        s = Clean(s);
        s = Regex.Replace(s, @"\s+CPU\s*@.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"^\d+(st|nd|rd|th) Gen\s+", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+w/.*$", "");
        s = Regex.Replace(s, @"\s+\d+-Core Processor$|\s+Processor$", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }
}
