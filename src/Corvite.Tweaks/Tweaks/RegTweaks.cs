using Corvite.Core.Hardware;
using Microsoft.Win32;

namespace Corvite.Tweaks.Tweaks;

/// <summary>One registry value that belongs to a toggle. Off = value Off, or delete the value when Off is null.</summary>
internal sealed record RegEntry(string Hive, string Path, string Name, object On, object? Off, RegistryValueKind Kind = RegistryValueKind.DWord);

/// <summary>Generic "set these registry values" toggle. Read() = 1 only when every entry holds its On value.</summary>
internal class RegToggleTweak : ITweak
{
    private readonly Func<IReadOnlyList<RegEntry>> _entries;
    public string Id { get; }
    public bool RequiresReboot { get; }

    public RegToggleTweak(string id, IReadOnlyList<RegEntry> entries, bool reboot = false)
        : this(id, () => entries, reboot) { }

    public RegToggleTweak(string id, Func<IReadOnlyList<RegEntry>> entries, bool reboot = false)
    { Id = id; _entries = entries; RequiresReboot = reboot; }

    public double? Read()
    {
        var list = _entries();
        if (list.Count == 0) return null;
        foreach (var e in list)
        {
            Reg.TryGet(e.Hive, e.Path, e.Name, out var v, out _);
            if (!Reg.Eq(v, e.On)) return 0;
        }
        return 1;
    }

    public string Capture() => Reg.ToJson(_entries().Select(e => Reg.Snap(e.Hive, e.Path, e.Name)));

    public TweakResult Apply(double value)
    {
        try
        {
            bool on = value >= 0.5;
            foreach (var e in _entries())
            {
                if (on) Reg.Set(e.Hive, e.Path, e.Name, e.On, e.Kind);
                else if (e.Off == null) Reg.Delete(e.Hive, e.Path, e.Name);
                else Reg.Set(e.Hive, e.Path, e.Name, e.Off, e.Kind);
            }
            return TweakResult.Success;
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Restore(string captured)
    {
        try
        {
            foreach (var s in Reg.FromJson(captured)) Reg.RestoreSnap(s);
            return TweakResult.Success;
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }
}

/// <summary>Nagle: the values live on every network interface key.</summary>
internal sealed class NagleTweak : RegToggleTweak
{
    private const string Base = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    public NagleTweak() : base("nagle", Build) { }

    private static IReadOnlyList<RegEntry> Build()
    {
        var list = new List<RegEntry>();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Base);
            if (k == null) return list;
            foreach (var name in k.GetSubKeyNames())
            {
                var p = Base + "\\" + name;
                list.Add(new RegEntry("HKLM", p, "TcpAckFrequency", 1, null));
                list.Add(new RegEntry("HKLM", p, "TCPNoDelay", 1, null));
            }
        }
        catch { /* ignore */ }
        return list;
    }
}

internal static class RegTweakFactory
{
    private const string GameCfg = @"System\GameConfigStore";
    private const string Mm = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    public static IEnumerable<ITweak> Create()
    {
        yield return new RegToggleTweak("gamemode", new[]
        {
            new RegEntry("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 0),
            new RegEntry("HKCU", @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, 0),
        });

        yield return new RegToggleTweak("dvr", new[]
        {
            new RegEntry("HKCU", GameCfg, "GameDVR_Enabled", 0, 1),
            new RegEntry("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, 1),
        });

        yield return new RegToggleTweak("hags", new[]
        {
            new RegEntry("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1),
        }, reboot: true);

        yield return new RegToggleTweak("fso", new[]
        {
            new RegEntry("HKCU", GameCfg, "GameDVR_FSEBehaviorMode", 2, 0),
            new RegEntry("HKCU", GameCfg, "GameDVR_HonorUserFSEBehaviorMode", 1, 0),
            new RegEntry("HKCU", GameCfg, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0),
        });

        yield return new RegToggleTweak("timer", new[]
        {
            new RegEntry("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1, null),
        }, reboot: true);

        yield return new RegToggleTweak("fast", new[]
        {
            new RegEntry("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1),
        }, reboot: true);

        yield return new NagleTweak();

        yield return new RegToggleTweak("nthr", new[]
        {
            new RegEntry("HKLM", Mm, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10),
        });

        yield return new RegToggleTweak("telem", new[]
        {
            new RegEntry("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null),
        });
    }
}
