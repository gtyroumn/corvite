using System.Runtime.InteropServices;

namespace Corvite.App.Services;

/// <summary>Whether the computer runs on battery (a common reason for low FPS on laptops).</summary>
public static class PowerInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static bool OnBattery()
    {
        try { return GetSystemPowerStatus(out var s) && s.AcLineStatus == 0; }
        catch { return false; }
    }
}
