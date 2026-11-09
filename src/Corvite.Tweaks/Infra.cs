using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Corvite.Tweaks;

public static class AppPaths
{
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Corvite");
    public static string BackupDir => Path.Combine(DataDir, "Backups");
    public static string LogDir => Path.Combine(DataDir, "Logs");
    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static void Ensure(string dir) { try { Directory.CreateDirectory(dir); } catch { /* ignore */ } }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.Ensure(AppPaths.LogDir);
                File.AppendAllText(Path.Combine(AppPaths.LogDir, "corvite.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break the app */ }
    }
}

/// <summary>Small key/value store (state.json) for facts a tweak must remember between runs, e.g. the power plan that was active before.</summary>
public static class StateStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _d;

    private static Dictionary<string, string> Data()
    {
        if (_d != null) return _d;
        try
        {
            if (File.Exists(AppPaths.StateFile))
                _d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AppPaths.StateFile));
        }
        catch { /* corrupt file: start empty */ }
        return _d ??= new Dictionary<string, string>();
    }

    public static string? Get(string key)
    {
        lock (Gate) return Data().TryGetValue(key, out var v) ? v : null;
    }

    public static void Set(string key, string value)
    {
        lock (Gate) { Data()[key] = value; Save(); }
    }

    public static void Remove(string key)
    {
        lock (Gate) { if (Data().Remove(key)) Save(); }
    }

    private static void Save()
    {
        try
        {
            AppPaths.Ensure(AppPaths.DataDir);
            File.WriteAllText(AppPaths.StateFile, JsonSerializer.Serialize(_d));
        }
        catch { /* ignore */ }
    }
}

internal readonly record struct ProcResult(int Code, string Out, string Err)
{
    public bool Ok => Code == 0;
}

/// <summary>Runs a console tool without a window and captures its output (decoded with the OEM code page).</summary>
internal static class Proc
{
    private static readonly Encoding Oem = GetOem();

    private static Encoding GetOem()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return Encoding.UTF8; }
    }

    public static ProcResult Run(string file, string args, int timeoutMs = 30000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Oem,
                StandardErrorEncoding = Oem
            };
            using var p = Process.Start(psi);
            if (p == null) return new ProcResult(-1, "", "process did not start");
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return new ProcResult(-2, "", "timeout");
            }
            p.WaitForExit();
            return new ProcResult(p.ExitCode, o.Result, e.Result);
        }
        catch (Exception ex)
        {
            return new ProcResult(-1, "", ex.Message);
        }
    }
}

internal static class Ps
{
    /// <summary>Runs a PowerShell script (passed as -EncodedCommand so quoting never breaks) and returns its output.</summary>
    public static ProcResult Run(string script, int timeoutMs = 30000)
    {
        var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue';" + script));
        return Proc.Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {enc}", timeoutMs);
    }
}
