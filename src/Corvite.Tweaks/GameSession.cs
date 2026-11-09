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
    private bool _toastOff;
    private readonly List<(string Name, string Exe)> _killed = new();
    private readonly Dictionary<int, ProcessPriorityClass> _lowered = new();
    private DateTime _lastLower = DateTime.MinValue;

    private const string ToastKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    // Never lowered by the background limit: system, drivers, audio, overlays, recorders, our own process.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "audiodg",
        "fontdrvhost", "explorer", "sihost", "ctfmon", "MsMpEng", "NisSrv", "SecurityHealthService", "Corvite",
        "NVDisplay.Container", "nvcontainer", "NVIDIA Web Helper", "RadeonSoftware", "AMDRSServ", "obs64", "obs32", "Discord",
        "RTSS", "MSIAfterburner", "steam", "EpicGamesLauncher", "GalaxyClient", "Origin", "EADesktop", "Battle.net"
    };

    public static string KillListFile => Path.Combine(AppPaths.DataDir, "kill-list.txt");

    public event Action<string>? GameStarted;
    public event Action<string>? GameEnded;

    public string? CurrentGame => _game == null ? null : SafeName(_game);

    public GameSession(bool dryRun)
    {
        _dry = dryRun;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        RecoverAfterCrash();
        _timer.Change(3000, 3000);
    }

    /// <summary>If the app died during a game, put back what it had switched off.</summary>
    private static void RecoverAfterCrash()
    {
        try
        {
            if (StateStore.Get("gs.upd") == "1") { Proc.Run("sc", "start wuauserv"); StateStore.Remove("gs.upd"); }
            if (StateStore.Get("gs.idx") == "1") { Proc.Run("sc", "start WSearch"); StateStore.Remove("gs.idx"); }
            var t = StateStore.Get("gs.toast");
            if (t != null) { Reg.Set("HKCU", ToastKey, "ToastEnabled", int.Parse(t), Microsoft.Win32.RegistryValueKind.DWord); StateStore.Remove("gs.toast"); }
        }
        catch (Exception ex) { Log.Info("recover: " + ex.Message); }
    }

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

            if (SessionSettings.Get("upd") >= 0.5 && StopService("wuauserv")) { _stoppedUpdate = true; StateStore.Set("gs.upd", "1"); }
            if (SessionSettings.Get("idx") >= 0.5 && StopService("WSearch")) { _stoppedIndex = true; StateStore.Set("gs.idx", "1"); }
            if (SessionSettings.Get("focus") >= 0.5) SetToasts(false);
            if (SessionSettings.Get("kill") >= 0.5) KillBackground(pid);
            if (SessionSettings.Get("bgcap") > 0) LowerBackground(pid);
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
            if (_stoppedUpdate) { Proc.Run("sc", "start wuauserv"); _stoppedUpdate = false; StateStore.Remove("gs.upd"); }
            if (_stoppedIndex) { Proc.Run("sc", "start WSearch"); _stoppedIndex = false; StateStore.Remove("gs.idx"); }
            if (_toastOff) SetToasts(true);
            RestoreLowered();
            RestartKilled();
        }
        GameEnded?.Invoke(name);
    }

    private void DuringGame()
    {
        if (!_dry && SessionSettings.Get("bgcap") > 0 && DateTime.Now - _lastLower > TimeSpan.FromSeconds(30) && _game != null)
            LowerBackground(_game.Id); // programs started after the game
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

    // ---------------------------------------------------------------- notifications (focus)
    private void SetToasts(bool on)
    {
        try
        {
            if (!on)
            {
                Reg.TryGet("HKCU", ToastKey, "ToastEnabled", out var cur, out _);
                StateStore.Set("gs.toast", cur == null ? "1" : Convert.ToInt32(cur).ToString());
                Reg.Set("HKCU", ToastKey, "ToastEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                _toastOff = true;
            }
            else
            {
                var prev = StateStore.Get("gs.toast") ?? "1";
                Reg.Set("HKCU", ToastKey, "ToastEnabled", int.Parse(prev), Microsoft.Win32.RegistryValueKind.DWord);
                StateStore.Remove("gs.toast");
                _toastOff = false;
            }
        }
        catch (Exception ex) { Log.Info("toasts: " + ex.Message); }
    }

    // ---------------------------------------------------------------- close background apps (kill)
    private static List<string> LoadKillList()
    {
        try
        {
            AppPaths.Ensure(AppPaths.DataDir);
            if (!File.Exists(KillListFile))
                File.WriteAllText(KillListFile,
                    "# Corvite: programs closed when a game starts and started again after it. One process name per line (without .exe).\r\n" +
                    "# Lines starting with # are ignored. Remove the # to enable a line.\r\n" +
                    "OneDrive\r\nDropbox\r\nGoogleDriveFS\r\nms-teams\r\nTeams\r\nSkype\r\nSlack\r\nViber\r\n" +
                    "#Telegram\r\n#Discord\r\n#chrome\r\n#msedge\r\n#firefox\r\n#Spotify\r\n");
            return File.ReadAllLines(KillListFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        }
        catch { return new(); }
    }

    private void KillBackground(int gamePid)
    {
        foreach (var name in LoadKillList())
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.Id == gamePid || p.Id == Environment.ProcessId) continue;
                    var exe = p.MainModule?.FileName ?? "";
                    var closed = p.CloseMainWindow() && p.WaitForExit(3000);
                    if (!closed && !p.HasExited) p.Kill(true);
                    if (exe.Length > 0 && !_killed.Any(k => k.Exe == exe)) _killed.Add((name, exe));
                    Log.Info($"closed {name} (pid {p.Id})");
                }
                catch (Exception ex) { Log.Info($"close {name}: {ex.Message}"); }
            }
        }
    }

    private void RestartKilled()
    {
        foreach (var (name, exe) in _killed)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); Log.Info($"restarted {name}"); }
            catch (Exception ex) { Log.Info($"restart {name}: {ex.Message}"); }
        }
        _killed.Clear();
    }

    // ---------------------------------------------------------------- background load limit (bgcap)
    /// <summary>1-24 % lowers other user programs to Below normal, 25-50 % to Idle. Restored when the game ends.</summary>
    private void LowerBackground(int gamePid)
    {
        _lastLower = DateTime.Now;
        var level = SessionSettings.Get("bgcap") >= 25 ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal;
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4 || p.Id == gamePid || p.Id == Environment.ProcessId || p.SessionId != session) continue;
                if (Protected.Contains(p.ProcessName) || _lowered.ContainsKey(p.Id)) continue;
                var cur = p.PriorityClass;
                if (cur == ProcessPriorityClass.Idle || cur == ProcessPriorityClass.BelowNormal) continue;
                p.PriorityClass = level;
                _lowered[p.Id] = cur;
            }
            catch { /* protected or already gone */ }
        }
        Log.Info($"background limit: {_lowered.Count} processes -> {level}");
    }

    private void RestoreLowered()
    {
        foreach (var (pid, cls) in _lowered)
        {
            try { using var p = Process.GetProcessById(pid); p.PriorityClass = cls; } catch { /* exited */ }
        }
        _lowered.Clear();
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
