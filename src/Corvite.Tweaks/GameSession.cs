using System.Diagnostics;
using System.Runtime.InteropServices;
using Corvite.Tweaks.Tweaks;

namespace Corvite.Tweaks;

/// <summary>
/// Detects a running game (a foreground window that covers a whole monitor, not a browser/player/shell) and applies the
/// "during the game" options: process priority, stopping Windows Update / Search indexing, temp cleanup, standby-list purging.
/// Everything it changes is put back when the game process exits or the app closes.
/// </summary>
public sealed class GameSession : IDisposable
{
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "Corvite", "dwm", "ApplicationFrameHost", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "vlc", "mpc-hc64", "mpc-hc", "mpv", "wmplayer", "Video.UI",
        "POWERPNT", "Teams", "ms-teams", "Zoom", "LockApp", "TextInputHost", "SystemSettings", "mstsc", "devenv", "Code"
    };

    private readonly bool _dry;
    private readonly System.Threading.Timer _timer;
    private int _candPid, _candTicks;
    private Process? _game;
    private bool _stoppedUpdate, _stoppedIndex;
    private DateTime _lastPurge = DateTime.MinValue;
    private int _busy;

    public event Action<string>? GameStarted;
    public event Action<string>? GameEnded;

    public string? CurrentGame => _game == null ? null : SafeName(_game);

    public GameSession(bool dryRun)
    {
        _dry = dryRun;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(3000, 3000);

    private static string SafeName(Process p) { try { return p.ProcessName; } catch { return "game"; } }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (_game != null)
            {
                bool gone;
                try { gone = _game.HasExited; } catch { gone = true; }
                if (gone) EndGame();
                else DuringGame();
                return;
            }
            var pid = FullscreenForegroundPid();
            if (pid == 0) { _candPid = 0; _candTicks = 0; return; }
            if (pid == _candPid) _candTicks++; else { _candPid = pid; _candTicks = 1; }
            if (_candTicks >= 2) BeginGame(pid); // about 6 s of full-screen: not a transition or a video
        }
        catch (Exception ex) { Log.Info("game tick: " + ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private void BeginGame(int pid)
    {
        Process p;
        try { p = Process.GetProcessById(pid); } catch { return; }
        var name = SafeName(p);
        if (Ignore.Contains(name)) { _candTicks = 0; return; }
        _game = p;
        Log.Info($"game detected: {name} (pid {pid}), dryRun={_dry}");

        if (!_dry)
        {
            try
            {
                var prio = (int)SessionSettings.Get("prio");
                if (prio > 0)
                {
                    p.PriorityClass = prio switch { 1 => ProcessPriorityClass.AboveNormal, 2 => ProcessPriorityClass.High, _ => ProcessPriorityClass.RealTime };
                    Log.Info($"priority -> {p.PriorityClass}");
                }
            }
            catch (Exception ex) { Log.Info("priority failed: " + ex.Message); }

            if (SessionSettings.Get("upd") >= 0.5 && StopService("wuauserv")) _stoppedUpdate = true;
            if (SessionSettings.Get("idx") >= 0.5 && StopService("WSearch")) _stoppedIndex = true;
            if (SessionSettings.Get("tmp") >= 0.5) CleanTemp();
        }
        GameStarted?.Invoke(name);
    }

    private void EndGame()
    {
        var name = _game == null ? "game" : SafeName(_game);
        Log.Info($"game ended: {name}");
        _game = null; _candPid = 0; _candTicks = 0;
        if (!_dry)
        {
            if (_stoppedUpdate) { Proc.Run("sc", "start wuauserv"); _stoppedUpdate = false; }
            if (_stoppedIndex) { Proc.Run("sc", "start WSearch"); _stoppedIndex = false; }
        }
        GameEnded?.Invoke(name);
    }

    private void DuringGame()
    {
        if (_dry || SessionSettings.Get("standby") < 0.5) return;
        if (DateTime.Now - _lastPurge < TimeSpan.FromSeconds(30)) return;
        var aggr = SessionSettings.Get("aggr");
        if (aggr < 5) aggr = 50;
        var m = Mem.Read();
        if (m == null) return;
        // 5 % -> purge when standby exceeds ~48 % of RAM; 100 % -> ~10 %
        var limit = m.Value.TotalBytes * (0.5 - aggr / 100.0 * 0.4);
        if (m.Value.StandbyBytes > Math.Max(limit, 512.0 * 1024 * 1024))
        {
            _lastPurge = DateTime.Now;
            var ok = Mem.PurgeStandby();
            Log.Info($"standby purge ({m.Value.StandbyBytes / 1048576.0:0} MB): {(ok ? "ok" : "failed")}");
        }
    }

    private static bool StopService(string name)
    {
        var q = Proc.Run("sc", $"query {name}");
        if (!q.Ok || !q.Out.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return false; // already stopped: do not start it later
        return Proc.Run("sc", $"stop {name}").Ok;
    }

    private static void CleanTemp()
    {
        long freed = 0;
        foreach (var dir in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (DateTime.Now - fi.LastWriteTime < TimeSpan.FromDays(1)) continue; // never touch fresh files
                        var len = fi.Length; fi.Delete(); freed += len;
                    }
                    catch { /* in use: skip */ }
                }
            }
            catch { /* folder missing or denied */ }
        }
        Log.Info($"temp cleanup: {freed / 1048576.0:0.#} MB");
    }

    public void Dispose()
    {
        _timer.Dispose();
        if (_game != null) EndGame();
    }

    // ---------------------------------------------------------------- full-screen detection
    private static int FullscreenForegroundPid()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return 0;
        var mon = MonitorFromWindow(h, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return 0;
        bool full = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        if (!full) return 0;
        GetWindowThreadProcessId(h, out var pid);
        return (int)pid;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
}

internal static class Mem
{
    public readonly record struct Info(double TotalBytes, double StandbyBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryListInfo
    {
        public UIntPtr Zero, Free, Modified, ModifiedNoWrite;
        public uint BadPageCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] ByPriority;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public UIntPtr[] Repurposed;
        public UIntPtr ModifiedPageFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemStatus
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int cls, ref MemoryListInfo info, int len, out int ret);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int cls, ref int info, int len);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemStatus s);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? sys, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr t, bool all, ref TokenPriv n, int len, IntPtr prev, IntPtr ret);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPriv { public int Count; public long Luid; public int Attr; }

    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private static bool _priv;

    public static Info? Read()
    {
        var ms = new MemStatus { dwLength = (uint)Marshal.SizeOf<MemStatus>() };
        if (!GlobalMemoryStatusEx(ref ms)) return null;
        var mi = new MemoryListInfo { ByPriority = new UIntPtr[8], Repurposed = new UIntPtr[8] };
        if (NtQuerySystemInformation(SystemMemoryListInformation, ref mi, Marshal.SizeOf<MemoryListInfo>(), out _) != 0) return null;
        double pages = 0;
        foreach (var p in mi.ByPriority) pages += (ulong)p;
        return new Info(ms.ullTotalPhys, pages * 4096.0);
    }

    public static bool PurgeStandby()
    {
        if (!_priv) _priv = Enable("SeProfileSingleProcessPrivilege");
        var cmd = MemoryPurgeStandbyList;
        return NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int)) == 0;
    }

    private static bool Enable(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var tok)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var tp = new TokenPriv { Count = 1, Luid = luid, Attr = 2 };
            return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(tok); }
    }
}
