using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Corvite.App.Setup;

public sealed record InstallOptions(string Dir, string Language, bool DesktopShortcut, bool DryRun);
public sealed record UninstallOptions(bool RestoreSettings, bool DeleteData, bool DryRun);

/// <summary>
/// Install / uninstall logic. Corvite-Setup.exe is the same single file as Corvite.exe: when started under the setup
/// name it shows the installer and copies itself into the chosen folder. Uninstall is "Corvite.exe --uninstall".
/// </summary>
public static class Installer
{
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Corvite";
    private static readonly string[] OwnFiles = { "Corvite.exe", "LICENSE.txt", "THIRD-PARTY-NOTICES.md" };

    public static string SelfPath => Environment.ProcessPath ?? "";
    public static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Corvite");
    private static string StartMenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Corvite");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Corvite.lnk");
    private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Corvite");

    /// <summary>Folder of an existing installation (from the uninstall entry), or null.</summary>
    public static string? InstalledDir()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(UninstallKey); return k?.GetValue("InstallLocation") as string; }
        catch { return null; }
    }

    /// <summary>True when this process runs from the installed folder (so "Uninstall" makes sense; a portable copy has nothing to uninstall).</summary>
    public static bool RunningInstalled()
    {
        try
        {
            var dir = InstalledDir();
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(exe)) return false;
            return Path.GetFullPath(exe).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Starts the installed copy in uninstall mode (the same window as the "Apps" entry opens).</summary>
    public static bool StartUninstall()
    {
        try
        {
            var dir = InstalledDir();
            var exe = dir == null ? null : Path.Combine(dir, "Corvite.exe");
            if (exe == null || !File.Exists(exe)) return false;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--uninstall") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>The program always lives in a folder called "Corvite": picking "D:\Games" installs to "D:\Games\Corvite".</summary>
    public static string NormalizeDir(string dir)
    {
        dir = dir.Trim().TrimEnd('\\', '/');
        return string.Equals(Path.GetFileName(dir), "Corvite", StringComparison.OrdinalIgnoreCase) ? dir : Path.Combine(dir, "Corvite");
    }

    /// <summary>The installer copies itself, so it must run as one published file (not from a debug build folder).</summary>
    private static bool IsSingleFile => Assembly.GetExecutingAssembly().Location.Length == 0;

    // ------------------------------------------------------------------ install
    public static async Task InstallAsync(InstallOptions o, IProgress<(double Pct, string Text)> p)
    {
        p.Report((2, "Preparing…"));
        if (o.DryRun) { await Simulate(p, "Copying files…", "Creating shortcuts…", "Registering…"); return; }

        if (!IsSingleFile || !File.Exists(SelfPath))
            throw new InvalidOperationException("Run Corvite-Setup.exe from the release download: the installer copies itself, so it cannot run from a build folder.");

        await Task.Run(() => StopRunning());
        var dir = NormalizeDir(o.Dir);
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "Corvite.exe");

        // copy this very file
        var tmp = exe + ".new";
        await using (var inp = new FileStream(SelfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        await using (var outp = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            var buf = new byte[1 << 20];
            long total = inp.Length, done = 0;
            int n;
            while ((n = await inp.ReadAsync(buf)) > 0)
            {
                await outp.WriteAsync(buf.AsMemory(0, n));
                done += n;
                p.Report((8 + 72.0 * done / total, "Copying files…"));
            }
        }
        File.Move(tmp, exe, true);
        ExtractResource("LICENSE.txt", Path.Combine(dir, "LICENSE.txt"));
        ExtractResource("THIRD-PARTY-NOTICES.md", Path.Combine(dir, "THIRD-PARTY-NOTICES.md"));

        p.Report((84, "Creating shortcuts…"));
        Directory.CreateDirectory(StartMenuDir);
        CreateShortcut(Path.Combine(StartMenuDir, "Corvite.lnk"), exe, dir);
        if (o.DesktopShortcut) CreateShortcut(DesktopLink, exe, dir);

        p.Report((92, "Registering the app…"));
        RegisterUninstall(dir, exe);
        WriteLanguage(o.Language);
        p.Report((100, "Done"));
    }

    private static void ExtractResource(string name, string path)
    {
        using var s = typeof(Installer).Assembly.GetManifestResourceStream(name);
        if (s == null) return;
        using var f = File.Create(path);
        s.CopyTo(f);
    }

    private static void CreateShortcut(string lnk, string target, string workDir)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Cannot create shortcuts on this system.");
        dynamic sh = Activator.CreateInstance(t)!;
        dynamic s = sh.CreateShortcut(lnk);
        s.TargetPath = target;
        s.WorkingDirectory = workDir;
        s.Description = "Corvite";
        s.IconLocation = target + ",0";
        s.Save();
    }

    private static void RegisterUninstall(string dir, string exe)
    {
        using var k = Registry.LocalMachine.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", "Corvite");
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", "Corvite contributors");
        k.SetValue("InstallLocation", dir);
        k.SetValue("DisplayIcon", exe);
        k.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        k.SetValue("URLInfoAbout", "https://github.com/gtyroumn/corvite");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try { k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord); } catch { }
    }

    /// <summary>Writes the chosen interface language into config.json, keeping every other setting.</summary>
    private static void WriteLanguage(string lang)
    {
        Directory.CreateDirectory(DataDir);
        var path = Path.Combine(DataDir, "config.json");
        JsonObject obj;
        try { obj = File.Exists(path) ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()) : new JsonObject(); }
        catch { obj = new JsonObject(); }
        obj["Language"] = lang;
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Closes other running copies of Corvite (an update over a running app).</summary>
    private static void StopRunning()
    {
        var me = Environment.ProcessId;
        foreach (var pr in Process.GetProcessesByName("Corvite"))
        {
            if (pr.Id == me) continue;
            try
            {
                pr.CloseMainWindow();
                if (!pr.WaitForExit(3000)) { pr.Kill(); pr.WaitForExit(3000); }
            }
            catch { /* already gone */ }
        }
    }

    // ------------------------------------------------------------------ uninstall
    public static async Task UninstallAsync(UninstallOptions o, IProgress<(double Pct, string Text)> p)
    {
        p.Report((2, "Preparing…"));
        if (o.DryRun) { await Simulate(p, "Restoring settings…", "Removing shortcuts…", "Unregistering…"); return; }

        await Task.Run(() => StopRunning());
        var failed = false;
        if (o.RestoreSettings)
        {
            p.Report((15, "Restoring Windows settings…"));
            var code = await Task.Run(() => Corvite.App.App.RestoreBaseline());
            failed = code == 1;
        }

        p.Report((60, "Removing shortcuts…"));
        TryDelete(Path.Combine(StartMenuDir, "Corvite.lnk"));
        try { if (Directory.Exists(StartMenuDir)) Directory.Delete(StartMenuDir, false); } catch { }
        TryDelete(DesktopLink);

        p.Report((75, "Unregistering the app…"));
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }

        if (o.DeleteData)
        {
            p.Report((85, "Deleting settings and backups…"));
            try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch { }
        }

        ScheduleFileRemoval();
        p.Report((100, failed ? "Done (some settings could not be restored)" : "Done"));
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    /// <summary>The running exe cannot delete itself: a hidden cmd waits a few seconds, deletes only Corvite's own files and removes the folder if it is then empty.</summary>
    private static void ScheduleFileRemoval()
    {
        var dir = Path.GetDirectoryName(SelfPath);
        if (string.IsNullOrEmpty(dir) || !string.Equals(Path.GetFileName(dir), "Corvite", StringComparison.OrdinalIgnoreCase)) return;
        var dels = string.Join(" & ", OwnFiles.Select(f => $"del /f /q \"{Path.Combine(dir, f)}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 >nul & {dels} & rmdir \"{dir}\"")
        { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static async Task Simulate(IProgress<(double Pct, string Text)> p, params string[] steps)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            p.Report((10 + 85.0 * i / steps.Length, steps[i]));
            await Task.Delay(700);
        }
        p.Report((100, "Done"));
    }
}
