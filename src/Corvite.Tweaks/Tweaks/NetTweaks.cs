namespace Corvite.Tweaks.Tweaks;

/// <summary>TCP auto-tuning: ON = Normal. The previous level is captured and restored exactly.</summary>
internal sealed class TcpAutoTweak : ITweak
{
    public string Id => "tcpauto";
    public bool RequiresReboot => false;

    private static string? Level()
    {
        var r = Ps.Run("(Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal");
        var s = r.Out.Trim();
        return r.Ok && s.Length > 0 ? s : null;
    }

    private static TweakResult Set(string level)
    {
        var r = Proc.Run("netsh", $"int tcp set global autotuninglevel={level.ToLowerInvariant()}");
        return r.Ok ? TweakResult.Success : TweakResult.Fail("netsh: " + (r.Err + r.Out).Trim());
    }

    public double? Read() => Level() is { } l ? (l.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? 1 : 0) : null;
    public string Capture() => Level() ?? "";

    public TweakResult Apply(double value)
    {
        if (value >= 0.5)
        {
            var cur = Level();
            if (cur != null && !cur.Equals("Normal", StringComparison.OrdinalIgnoreCase)) StateStore.Set("tcpauto.prev", cur);
            return Set("normal");
        }
        // Off = the level the system had before we touched it (baseline), else Normal is already the Windows default.
        var prev = StateStore.Get("tcpauto.prev");
        return string.IsNullOrEmpty(prev) ? TweakResult.Success : Set(prev);
    }

    public TweakResult Restore(string captured) => string.IsNullOrEmpty(captured) ? TweakResult.Success : Set(captured);
}

/// <summary>Fast DNS (1.1.1.1 / 1.0.0.1) on active physical adapters. DHCP-provided DNS is told apart from static via the registry NameServer value.</summary>
internal sealed class DnsTweak : ITweak
{
    private const string Servers = "1.1.1.1,1.0.0.1";
    public string Id => "dns";
    public bool RequiresReboot => false;

    private const string List =
        "Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { " +
        "$ns=(Get-ItemProperty ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\'+$_.InterfaceGuid) -ErrorAction SilentlyContinue).NameServer; " +
        "'{0}|{1}' -f $_.ifIndex, (($ns -replace '\\s+',',').Trim(',')) }";

    private static List<(int Idx, string Ns)> Adapters()
    {
        var r = Ps.Run(List);
        var res = new List<(int, string)>();
        if (!r.Ok) return res;
        foreach (var line in r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim().Split('|');
            if (p.Length == 2 && int.TryParse(p[0], out var i)) res.Add((i, p[1].Trim()));
        }
        return res;
    }

    public double? Read()
    {
        var a = Adapters();
        if (a.Count == 0) return null;
        return a.All(x => x.Ns == Servers) ? 1 : 0;
    }

    public string Capture() => string.Join(";", Adapters().Select(a => $"{a.Idx}|{a.Ns}"));

    public TweakResult Apply(double value)
    {
        var a = Adapters();
        if (a.Count == 0) return TweakResult.Fail("no active network adapter");
        if (value >= 0.5)
        {
            if (Read() == 0) StateStore.Set("dns.prev", Capture());
            var cmd = string.Join(";", a.Select(x => $"Set-DnsClientServerAddress -InterfaceIndex {x.Idx} -ServerAddresses {Servers}"));
            var r = Ps.Run(cmd);
            return r.Ok ? TweakResult.Success : TweakResult.Fail((r.Err + r.Out).Trim());
        }
        var prev = StateStore.Get("dns.prev");
        return string.IsNullOrEmpty(prev) ? ResetAll(a.Select(x => x.Idx)) : Restore(prev);
    }

    private static TweakResult ResetAll(IEnumerable<int> idx)
    {
        var r = Ps.Run(string.Join(";", idx.Select(i => $"Set-DnsClientServerAddress -InterfaceIndex {i} -ResetServerAddresses")));
        return r.Ok ? TweakResult.Success : TweakResult.Fail((r.Err + r.Out).Trim());
    }

    public TweakResult Restore(string captured)
    {
        if (string.IsNullOrWhiteSpace(captured)) return TweakResult.Success;
        var cmds = new List<string>();
        foreach (var part in captured.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split('|');
            if (p.Length != 2 || !int.TryParse(p[0], out var i)) continue;
            cmds.Add(string.IsNullOrWhiteSpace(p[1])
                ? $"Set-DnsClientServerAddress -InterfaceIndex {i} -ResetServerAddresses"
                : $"Set-DnsClientServerAddress -InterfaceIndex {i} -ServerAddresses {p[1]}");
        }
        if (cmds.Count == 0) return TweakResult.Success;
        var r = Ps.Run(string.Join(";", cmds));
        return r.Ok ? TweakResult.Success : TweakResult.Fail((r.Err + r.Out).Trim());
    }
}
