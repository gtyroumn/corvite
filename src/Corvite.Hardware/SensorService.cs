using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace Corvite.Hardware;

/// <summary>One polling result. Null = this sensor is not available on this machine.</summary>
public sealed record SensorSample(
    double? CpuTemp, double? CpuLoad, double? CpuClockMhz, double? CpuPowerW,
    double? GpuTemp, double? GpuLoad, double? GpuCoreMhz, double? GpuMemMhz, double? GpuVramMb,
    double RamUsedGb, double RamTotalGb, bool TempFromAcpi);

/// <summary>
/// Live monitoring (TZ 2): LibreHardwareMonitor for CPU/GPU, GlobalMemoryStatusEx for RAM,
/// ACPI thermal zone as a last-resort CPU temperature. Samples arrive on a worker thread.
/// </summary>
public sealed class SensorService : IDisposable
{
    private readonly Computer _pc = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    private Timer? _timer;
    private int _busy;
    private bool _acpiBroken;

    public event Action<SensorSample>? SampleReady;

    /// <summary>Throws if the sensor library cannot be opened (the caller falls back to demo data).</summary>
    public void Start(int intervalMs = 1500)
    {
        _pc.Open();
        _timer = new Timer(_ => Tick(), null, 0, intervalMs);
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { SampleReady?.Invoke(Read()); }
        catch { /* a failed poll is skipped */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private SensorSample Read()
    {
        double? cpuT = null, cpuL = null, cpuC = null, cpuP = null;
        double? gT = null, gL = null, gC = null, gM = null, gV = null;
        var gpuRank = -1;

        foreach (var hw in _pc.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
            var sensors = hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors)).Where(s => s.Value.HasValue).ToList();

            if (hw.HardwareType == HardwareType.Cpu && cpuT == null && cpuL == null)
            {
                var temps = sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value > 0).ToList();
                cpuT = Pick(temps, "CPU Package", "Tctl", "Tdie", "Core Average", "Core Max") ?? (temps.Count > 0 ? temps.Max(t => (double)t.Value!.Value) : (double?)null);
                cpuL = Pick(sensors.Where(s => s.SensorType == SensorType.Load), "CPU Total");
                var clocks = sensors.Where(s => s.SensorType == SensorType.Clock && s.Value > 0
                                                && (s.Name.StartsWith("CPU Core #") || s.Name.StartsWith("Core #"))).ToList();
                cpuC = clocks.Count > 0 ? clocks.Max(c => (double)c.Value!.Value) : null;
                cpuP = Pick(sensors.Where(s => s.SensorType == SensorType.Power), "CPU Package");
            }
            else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                var rank = hw.HardwareType == HardwareType.GpuNvidia ? 3 : hw.HardwareType == HardwareType.GpuAmd ? 2 : 1;
                if (rank <= gpuRank) continue;
                gpuRank = rank;
                gT = Pick(sensors.Where(s => s.SensorType == SensorType.Temperature), "GPU Core")
                     ?? sensors.Where(s => s.SensorType == SensorType.Temperature).Select(s => (double?)s.Value).FirstOrDefault();
                gL = Pick(sensors.Where(s => s.SensorType == SensorType.Load), "GPU Core", "D3D 3D")
                     ?? sensors.Where(s => s.SensorType == SensorType.Load).Select(s => (double?)s.Value).FirstOrDefault();
                gC = Pick(sensors.Where(s => s.SensorType == SensorType.Clock), "GPU Core");
                gM = Pick(sensors.Where(s => s.SensorType == SensorType.Clock), "GPU Memory");
                gV = Pick(sensors.Where(s => s.SensorType == SensorType.SmallData), "GPU Memory Total");
            }
        }

        var acpi = false;
        if (cpuT == null) { cpuT = AcpiTemp(); acpi = cpuT != null; }

        var (used, total) = Ram();
        return new SensorSample(cpuT, cpuL, cpuC, cpuP, gT, gL, gC, gM, gV, used, total, acpi);
    }

    /// <summary>First sensor whose name matches the given names in priority order (exact, then contains).</summary>
    private static double? Pick(IEnumerable<ISensor> sensors, params string[] names)
    {
        var list = sensors.Where(s => s.Value.HasValue).ToList();
        foreach (var n in names)
        {
            var hit = list.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                      ?? list.FirstOrDefault(s => s.Name.Contains(n, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit.Value!.Value;
        }
        return null;
    }

    /// <summary>Thermal zone fallback (tenths of Kelvin). Unreliable on some laptops, so values outside 10..110 °C are ignored.</summary>
    private double? AcpiTemp()
    {
        if (_acpiBroken) return null;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? best = null;
            foreach (ManagementObject o in s.Get())
            {
                var c = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                if (c is > 10 and < 110 && (best == null || c > best)) best = c;
            }
            if (best == null) _acpiBroken = true;
            return best;
        }
        catch { _acpiBroken = true; return null; }
    }

    // ---- RAM via GlobalMemoryStatusEx
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    private static (double Used, double Total) Ram()
    {
        var m = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(m)) return (0, 0);
        const double gb = 1073741824.0;
        return ((m.ullTotalPhys - m.ullAvailPhys) / gb, m.ullTotalPhys / gb);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        try { _pc.Close(); } catch { /* ignore */ }
    }
}
