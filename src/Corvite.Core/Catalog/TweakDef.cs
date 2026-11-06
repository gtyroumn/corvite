namespace Corvite.Core.Catalog;

public enum Section { Overview, Hardware, Windows, Cpu, Gpu, Memory, RamTimings, Network, Background }

public enum TweakKind { Toggle, Slider }

/// <summary>Vendor restriction of a tweak (TZ section 8).</summary>
public enum Vendor { Any, AmdCpu, IntelCpu, NvidiaGpu, AmdGpu }

public enum CpuVendor { Amd, Intel }

/// <summary>IntelArc = any Intel graphics (iGPU or Arc), Unknown = anything unsupported (AMD iGPU, unrecognised). Both hide the GPU section.</summary>
public enum GpuVendor { Nvidia, Amd, IntelArc, Unknown }

public enum PlatformKind { Pc, Laptop }

/// <summary>Detected (or demo) hardware used to filter tweaks and scale the AI/profile logic.</summary>
public sealed record HardwareProfile(CpuVendor Cpu, GpuVendor Gpu, int Tier);

/// <summary>
/// Static description of one tweak. All user-visible texts live in Strings.resx under
/// keys "tw.{Id}.name|sub|desc|stab|lat|temp|note|unit|lblN" (ru / uk / en).
/// </summary>
public sealed record TweakDef(
    string Id,
    Section Section,
    string GroupKey,
    Vendor Vendor,
    bool IsGuard,
    int FpsMin,
    int FpsMax,
    bool HasNote,
    TweakKind Kind,
    int Risk = 0,                // toggles: 0..2
    bool DefaultOn = false,      // toggles
    double Min = 0,              // sliders
    double Max = 1,
    double Step = 1,
    string? UnitKey = null,
    bool ShowPlus = false,
    int LabelCount = 0,          // sliders with named levels (game process priority)
    double DefaultValue = 0)
{
    public bool IsToggle => Kind == TweakKind.Toggle;
}
