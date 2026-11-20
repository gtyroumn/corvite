using System.Diagnostics;
using System.IO;

namespace Corvite.App.Services;

/// <summary>
/// An official manufacturer program that the tuning guides point to. Launch() starts it when it is found in its usual place,
/// otherwise opens the manufacturer's download page. Corvite never downloads or installs anything by itself.
/// </summary>
public sealed record OfficialApp(string NameKey, string[] ExePaths, string? AppsFolderId, string Url)
{
    public static readonly OfficialApp NvidiaPanel = new("app.nvcp",
        new[] { @"%ProgramFiles%\NVIDIA Corporation\Control Panel Client\nvcplui.exe" },
        "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel", VendorTools.NvidiaDrivers);

    public static readonly OfficialApp AmdSoftware = new("app.amdsw",
        new[] { @"%ProgramFiles%\AMD\CNext\CNext\RadeonSoftware.exe" }, null, VendorTools.AmdGpuDrivers);

    public static readonly OfficialApp RyzenMaster = new("app.rm",
        new[] { @"%ProgramFiles%\AMD\RyzenMaster\bin\AMD Ryzen Master.exe" }, null, VendorTools.RyzenMaster);

    public static readonly OfficialApp Xtu = new("app.xtu",
        new[] { @"%ProgramFiles%\Intel\Intel(R) Extreme Tuning Utility\Client\XtuUiLauncher.exe" }, null, VendorTools.Xtu);

    public void Launch()
    {
        try
        {
            foreach (var raw in ExePaths)
            {
                var path = Environment.ExpandEnvironmentVariables(raw);
                if (File.Exists(path)) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return; }
            }
            if (AppsFolderId != null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + AppsFolderId) { UseShellExecute = true });
                return;
            }
        }
        catch { /* fall through to the download page */ }
        OpenSite();
    }

    public void OpenSite()
    {
        var t = new VendorTool("", "", Url);
        if (!t.IsOfficial) return;
        try { Process.Start(new ProcessStartInfo(t.Url) { UseShellExecute = true }); } catch { /* no browser */ }
    }
}
