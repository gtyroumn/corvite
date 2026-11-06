using System.Text.Json;
using Microsoft.Win32;

namespace Corvite.Tweaks;

/// <summary>Snapshot of one registry value (exists or not, kind, value) for exact restore.</summary>
internal sealed class RegSnap
{
    public string H { get; set; } = "";
    public string P { get; set; } = "";
    public string N { get; set; } = "";
    public bool E { get; set; }
    public string K { get; set; } = "DWord";
    public string V { get; set; } = "";
}

internal static class Reg
{
    private static RegistryKey Root(string hive) => hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;

    public static bool TryGet(string hive, string path, string name, out object? value, out RegistryValueKind kind)
    {
        value = null; kind = RegistryValueKind.Unknown;
        try
        {
            using var k = Root(hive).OpenSubKey(path, false);
            if (k == null) return false;
            value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return false;
            kind = k.GetValueKind(name);
            return true;
        }
        catch { return false; }
    }

    public static void Set(string hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var k = Root(hive).CreateSubKey(path, true);
        k.SetValue(name, kind == RegistryValueKind.DWord ? ToDword(value) : value, kind);
    }

    public static void Delete(string hive, string path, string name)
    {
        try
        {
            using var k = Root(hive).OpenSubKey(path, true);
            k?.DeleteValue(name, false);
        }
        catch { /* ignore */ }
    }

    /// <summary>DWORD values are stored as signed ints; 0xFFFFFFFF becomes -1.</summary>
    public static int ToDword(object v) => unchecked((int)(Convert.ToInt64(v) & 0xFFFFFFFFL));

    private static bool IsNum(object o) => o is int or uint or long or ulong or short or ushort or byte;

    public static bool Eq(object? cur, object? want)
    {
        if (cur == null || want == null) return cur == null && want == null;
        if (IsNum(cur) && IsNum(want))
            return (Convert.ToInt64(cur) & 0xFFFFFFFFL) == (Convert.ToInt64(want) & 0xFFFFFFFFL);
        return string.Equals(cur.ToString(), want.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static RegSnap Snap(string hive, string path, string name)
    {
        var s = new RegSnap { H = hive, P = path, N = name };
        if (TryGet(hive, path, name, out var v, out var kind) && v != null)
        {
            s.E = true;
            s.K = kind.ToString();
            s.V = kind == RegistryValueKind.DWord ? (Convert.ToInt64(v) & 0xFFFFFFFFL).ToString() : v.ToString() ?? "";
        }
        return s;
    }

    public static void RestoreSnap(RegSnap s)
    {
        if (!s.E) { Delete(s.H, s.P, s.N); return; }
        var kind = Enum.TryParse<RegistryValueKind>(s.K, out var k) ? k : RegistryValueKind.DWord;
        if (kind == RegistryValueKind.DWord) Set(s.H, s.P, s.N, long.Parse(s.V), kind);
        else Set(s.H, s.P, s.N, s.V, RegistryValueKind.String);
    }

    public static string ToJson(IEnumerable<RegSnap> snaps) => JsonSerializer.Serialize(snaps);

    public static List<RegSnap> FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<List<RegSnap>>(json) ?? new(); }
        catch { return new(); }
    }
}
