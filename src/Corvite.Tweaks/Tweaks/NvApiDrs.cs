using System.Runtime.InteropServices;
using Corvite.Core.Catalog;
using Corvite.Core.Hardware;

namespace Corvite.Tweaks.Tweaks;

/// <summary>
/// NVIDIA's public NVAPI (nvapi64.dll, shipped with the driver; header published by NVIDIA under the MIT licence), driver-settings part:
/// the same "global profile" that the NVIDIA Control Panel edits. Function ids and the NVDRS_SETTING layout are taken from the
/// published nvapi_interface.h / nvapi.h (size 12320, version 12320 | 1 &lt;&lt; 16). Every call is guarded: a missing DLL or an
/// unknown status only makes the tweak unavailable. No kernel driver is involved.
/// </summary>
internal static class NvApi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr DQuery(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DCreate(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSession(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DProfile(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGet(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSet(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DRestore(IntPtr session, IntPtr profile, uint id);

    // layout of NVDRS_SETTING_V1 (#pragma pack 4), verified against the header
    private const int Size = 12320, OffId = 4100, OffType = 4104, OffLoc = 4108, OffCur = 8220;
    private const int Version = Size | (1 << 16);

    private static readonly object Gate = new();
    private static bool _tried, _ok;
    private static DCreate? _create;
    private static DSession? _destroy, _load, _save;
    private static DProfile? _global;
    private static DGet? _get;
    private static DSet? _set;
    private static DRestore? _restore;

    private static T? Fn<T>(DQuery q, uint id) where T : Delegate
    {
        var p = q(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public static bool Ready
    {
        get
        {
            lock (Gate)
            {
                if (_tried) return _ok;
                _tried = true;
                try
                {
                    var dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll");
                    if (!File.Exists(dll) || !NativeLibrary.TryLoad(dll, out var lib)) return false;
                    if (!NativeLibrary.TryGetExport(lib, "nvapi_QueryInterface", out var qi)) return false;
                    var query = Marshal.GetDelegateForFunctionPointer<DQuery>(qi);

                    var init = Fn<DInit>(query, 0x0150e828);
                    _create = Fn<DCreate>(query, 0x0694d52e);
                    _destroy = Fn<DSession>(query, 0xdad9cff8);
                    _load = Fn<DSession>(query, 0x375dbd6b);
                    _save = Fn<DSession>(query, 0xfcbc7e14);
                    _global = Fn<DProfile>(query, 0x617bff9f);
                    _get = Fn<DGet>(query, 0x73bf8338);
                    _set = Fn<DSet>(query, 0x577dd202);
                    _restore = Fn<DRestore>(query, 0x53f0381e);
                    if (init == null || _create == null || _destroy == null || _load == null || _save == null || _global == null
                        || _get == null || _set == null || _restore == null || init() != 0) return false;
                    return _ok = true;
                }
                catch (Exception ex) { Log.Info("NVAPI init failed: " + ex.Message); return false; }
            }
        }
    }

    /// <summary>Opens a session on the global profile, runs the action, optionally saves, always closes. Returns the last NVAPI status.</summary>
    private static int Session(Func<IntPtr, IntPtr, int> action, bool save)
    {
        if (!Ready) return -1;
        lock (Gate)
        {
            var r = _create!(out var s);
            if (r != 0) return r;
            try
            {
                r = _load!(s);
                if (r != 0) return r;
                r = _global!(s, out var p);
                if (r != 0) return r;
                r = action(s, p);
                if (r == 0 && save) r = _save!(s);
                return r;
            }
            finally { _destroy!(s); }
        }
    }

    private static IntPtr NewSetting(uint id)
    {
        var buf = Marshal.AllocHGlobal(Size);
        Marshal.Copy(new byte[Size], 0, buf, Size);
        Marshal.WriteInt32(buf, 0, Version);
        Marshal.WriteInt32(buf, OffId, unchecked((int)id));
        return buf;
    }

    /// <summary>Value stored explicitly in the global profile, or null when the driver default is in use.</summary>
    public static uint? Get(uint id)
    {
        uint? result = null;
        var buf = NewSetting(id);
        try
        {
            Session((s, p) =>
            {
                var r = _get!(s, p, id, buf);
                if (r == 0 && Marshal.ReadInt32(buf, OffLoc) == 0) result = unchecked((uint)Marshal.ReadInt32(buf, OffCur));
                return 0;   // "setting not found" simply means the default
            }, false);
        }
        finally { Marshal.FreeHGlobal(buf); }
        return result;
    }

    public static int Set(uint id, uint value)
    {
        var buf = NewSetting(id);
        try
        {
            Marshal.WriteInt32(buf, OffType, 0);                       // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(buf, OffLoc, 0);                        // current profile
            Marshal.WriteInt32(buf, OffCur, unchecked((int)value));
            return Session((s, p) => _set!(s, p, buf), true);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Back to the driver default. A setting that was never changed is already there, so "not found" is success.</summary>
    public static int Reset(uint id) => Session((s, p) => { var r = _restore!(s, p, id); return r == -160 ? 0 : r; }, true);

    public static string Err(int code) => code == -1 ? "NVIDIA driver interface not available" : $"NVAPI error {code}";
}

/// <summary>One DWORD setting of the NVIDIA global driver profile, shown as a switch (on = OnValue, off = driver default).</summary>
internal sealed class NvDrsTweak : ITweak, IConditionalTweak
{
    private readonly uint _setting, _on;
    public string Id { get; }
    public bool RequiresReboot => false;

    public NvDrsTweak(string id, uint setting, uint on) { Id = id; _setting = setting; _on = on; }

    public bool IsApplicable(HardwareInfo hw) => hw.GpuBrand == GpuVendor.Nvidia && NvApi.Ready;

    public double? Read() => NvApi.Ready ? (NvApi.Get(_setting) == _on ? 1 : 0) : null;

    public string Capture() => NvApi.Get(_setting)?.ToString() ?? "";

    public TweakResult Apply(double value)
    {
        var r = value >= 0.5 ? NvApi.Set(_setting, _on) : NvApi.Reset(_setting);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(NvApi.Err(r));
    }

    public TweakResult Restore(string captured)
    {
        var r = uint.TryParse(captured, out var v) ? NvApi.Set(_setting, v) : NvApi.Reset(_setting);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(NvApi.Err(r));
    }
}
