using System.Runtime.InteropServices;
using Corvite.Core.Catalog;
using Corvite.Core.Hardware;

namespace Corvite.Tweaks.Tweaks;

/// <summary>
/// Thin wrapper over NVML (nvml.dll, shipped with the NVIDIA driver). Every export is resolved at run time, so a missing
/// driver or an older driver without a function simply makes that tweak unavailable. NVML settings are not persistent:
/// they reset on reboot or driver reload, which is the safe behaviour for overclocking.
/// </summary>
internal static class Nvml
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DHandle(uint index, out IntPtr dev);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGetU(IntPtr dev, out uint v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DConstr(IntPtr dev, out uint min, out uint max);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSetU(IntPtr dev, uint v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DGetI(IntPtr dev, out int v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DSetI(IntPtr dev, int v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DMinMax(IntPtr dev, out int min, out int max);

    private static readonly object Gate = new();
    private static bool _tried;
    private static IntPtr _lib, _dev;

    private static DGetU? _getLimit, _getDefault;
    private static DConstr? _constraints;
    private static DSetU? _setLimit;
    private static DGetI? _getGpc, _getMem;
    private static DSetI? _setGpc, _setMem;
    private static DMinMax? _rangeGpc, _rangeMem;

    private static T? Fn<T>(string name) where T : Delegate =>
        NativeLibrary.TryGetExport(_lib, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;

    public static bool Ready
    {
        get
        {
            lock (Gate)
            {
                if (_tried) return _dev != IntPtr.Zero;
                _tried = true;
                try
                {
                    var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var found = new[] { Path.Combine(sys, "nvml.dll"), Path.Combine(pf, @"NVIDIA Corporation\NVSMI\nvml.dll") }
                        .Any(p => File.Exists(p) && NativeLibrary.TryLoad(p, out _lib));
                    if (!found) return false;

                    var init = Fn<DInit>("nvmlInit_v2");
                    var handle = Fn<DHandle>("nvmlDeviceGetHandleByIndex_v2");
                    if (init == null || handle == null || init() != 0 || handle(0, out _dev) != 0) { _dev = IntPtr.Zero; return false; }

                    _getLimit = Fn<DGetU>("nvmlDeviceGetPowerManagementLimit");
                    _getDefault = Fn<DGetU>("nvmlDeviceGetPowerManagementDefaultLimit");
                    _constraints = Fn<DConstr>("nvmlDeviceGetPowerManagementLimitConstraints");
                    _setLimit = Fn<DSetU>("nvmlDeviceSetPowerManagementLimit");
                    _getGpc = Fn<DGetI>("nvmlDeviceGetGpcClkVfOffset");
                    _setGpc = Fn<DSetI>("nvmlDeviceSetGpcClkVfOffset");
                    _getMem = Fn<DGetI>("nvmlDeviceGetMemClkVfOffset");
                    _setMem = Fn<DSetI>("nvmlDeviceSetMemClkVfOffset");
                    _rangeGpc = Fn<DMinMax>("nvmlDeviceGetGpcClkMinMaxVfOffset");
                    _rangeMem = Fn<DMinMax>("nvmlDeviceGetMemClkMinMaxVfOffset");
                    return true;
                }
                catch (Exception ex) { Log.Info("NVML init failed: " + ex.Message); _dev = IntPtr.Zero; return false; }
            }
        }
    }

    // ---- power limit (mW)
    public static bool PowerSupported => Ready && _getLimit != null && _getDefault != null && _constraints != null && _setLimit != null
                                         && _getLimit(_dev, out _) == 0 && _getDefault(_dev, out var d) == 0 && d > 0;
    public static uint? PowerLimit() => Ready && _getLimit != null && _getLimit(_dev, out var v) == 0 ? v : null;
    public static uint? PowerDefault() => Ready && _getDefault != null && _getDefault(_dev, out var v) == 0 ? v : null;
    public static (uint Min, uint Max)? PowerRange() => Ready && _constraints != null && _constraints(_dev, out var a, out var b) == 0 ? (a, b) : null;
    public static int SetPower(uint mw) => _setLimit!(_dev, mw);

    // ---- clock offsets (MHz, as NVML reports them)
    public static bool OffsetSupported(bool mem) => Ready && (mem ? _getMem : _getGpc) is { } g && g(_dev, out _) == 0 && (mem ? _setMem : _setGpc) != null;
    public static int? Offset(bool mem) => Ready && (mem ? _getMem : _getGpc) is { } g && g(_dev, out var v) == 0 ? v : null;
    public static (int Min, int Max)? OffsetRange(bool mem) => Ready && (mem ? _rangeMem : _rangeGpc) is { } r && r(_dev, out var a, out var b) == 0 ? (a, b) : null;
    public static int SetOffset(bool mem, int mhz) => (mem ? _setMem : _setGpc)!(_dev, mhz);

    public static string Err(int code) => code switch
    {
        3 => "not supported by this GPU/driver",
        4 => "administrator rights required",
        2 => "invalid value",
        _ => $"NVML error {code}"
    };
}

internal sealed class NvmlPowerTweak : ITweak, IConditionalTweak
{
    public string Id => "gpow";
    public bool RequiresReboot => false;
    public bool IsApplicable(HardwareInfo hw) => hw.GpuBrand == GpuVendor.Nvidia && Nvml.PowerSupported;

    public double? Read()
    {
        var cur = Nvml.PowerLimit(); var def = Nvml.PowerDefault();
        return cur == null || def == null || def == 0 ? null : Math.Round(cur.Value * 100.0 / def.Value);
    }

    public string Capture() => Nvml.PowerLimit()?.ToString() ?? "";

    public TweakResult Apply(double value)
    {
        var def = Nvml.PowerDefault(); var range = Nvml.PowerRange();
        if (def == null || range == null) return TweakResult.Fail("power limit not readable");
        var target = (uint)Math.Round(def.Value * value / 100.0);
        if (target > range.Value.Max) return TweakResult.Fail($"this GPU allows at most {range.Value.Max * 100.0 / def.Value:0}%");
        if (target < range.Value.Min) target = range.Value.Min;
        var r = Nvml.SetPower(target);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(Nvml.Err(r));
    }

    public TweakResult Restore(string captured)
    {
        if (!uint.TryParse(captured, out var mw)) return TweakResult.Success;
        var r = Nvml.SetPower(mw);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(Nvml.Err(r));
    }
}

/// <summary>
/// Core / memory clock offset. Core: slider MHz = NVML offset. Memory: the slider uses the effective (marketing) rate like
/// MSI Afterburner, NVML works in the real clock, which is half of it on GDDR6/6X, hence the factor 2.
/// </summary>
internal sealed class NvmlOffsetTweak : ITweak, IConditionalTweak
{
    private readonly bool _mem;
    public string Id { get; }
    public bool RequiresReboot => false;
    public NvmlOffsetTweak(bool mem) { _mem = mem; Id = mem ? "gmem" : "gcore"; }

    private double Factor => _mem ? 2 : 1;

    public bool IsApplicable(HardwareInfo hw) => hw.GpuBrand == GpuVendor.Nvidia && Nvml.OffsetSupported(_mem);

    public double? Read() => Nvml.Offset(_mem) is int o ? o * Factor : null;
    public string Capture() => Nvml.Offset(_mem)?.ToString() ?? "";

    public TweakResult Apply(double value)
    {
        var nv = (int)Math.Round(value / Factor);
        var range = Nvml.OffsetRange(_mem);
        if (range != null && nv > range.Value.Max) return TweakResult.Fail($"this GPU allows at most +{range.Value.Max * Factor:0} MHz");
        var r = Nvml.SetOffset(_mem, nv);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(Nvml.Err(r));
    }

    public TweakResult Restore(string captured)
    {
        if (!int.TryParse(captured, out var o)) return TweakResult.Success;
        var r = Nvml.SetOffset(_mem, o);
        return r == 0 ? TweakResult.Success : TweakResult.Fail(Nvml.Err(r));
    }
}

public static class NvmlProbe
{
    public static bool Present() => Nvml.Ready;
}
