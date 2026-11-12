using Corvite.Core.Catalog;
using Corvite.Core.Hardware;

namespace Corvite.App.Services;

/// <summary>An official manufacturer program or driver page. Keys are Strings.resx entries; Url is always on the manufacturer's own site.</summary>
public sealed record VendorTool(string NameKey, string WhyKey, string Url)
{
    private static readonly string[] AllowedHosts = { "intel.com", "amd.com", "nvidia.com", "softpedia.com" };

    public string Host => new Uri(Url).Host;

    /// <summary>Only the listed domains (manufacturers, plus the Softpedia catalogue page for Thaiphoon Burner) may be opened from the app.</summary>
    public bool IsOfficial => Uri.TryCreate(Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}

public static class VendorTools
{
    /// <summary>Thaiphoon Burner on Softpedia (a third-party software catalogue, chosen by the user): the original author's site is gone.</summary>
    public const string ThaiphoonUrl = "https://www.softpedia.com/get/Tweak/Memory-Tweak/Thaiphoon-Burner.shtml#download";

    private const string Xtu = "https://www.intel.com/content/www/us/en/download/17881/intel-extreme-tuning-utility-intel-xtu.html";
    private const string RyzenMaster = "https://www.amd.com/en/products/software/ryzen-master.html";
    private const string NvidiaDrivers = "https://www.nvidia.com/Download/index.aspx";
    private const string AmdGpuDrivers = "https://www.amd.com/en/resources/support-articles/faqs/GPU-131.html";

    /// <summary>
    /// Which official program explains / unlocks a locked tweak, chosen from the detected hardware
    /// (Intel CPU -> Intel, AMD CPU -> AMD, NVIDIA GPU -> NVIDIA, AMD GPU -> AMD). Null when there is nothing official to point to.
    /// </summary>
    public static VendorTool? Resolve(Section section, HardwareInfo hw, bool driverBound)
    {
        if (section == Section.Cpu)
            return hw.CpuBrand == CpuVendor.Intel
                ? new VendorTool("tool.xtu", "tool.xtu.why", Xtu)
                : new VendorTool("tool.rm", "tool.rm.why", RyzenMaster);

        if (section == Section.Gpu)
            return hw.GpuBrand switch
            {
                GpuVendor.Nvidia => new VendorTool("tool.nvdrv", driverBound ? "tool.nvprof.why" : "tool.nvdrv.why", NvidiaDrivers),
                GpuVendor.Amd => new VendorTool("tool.amdgpu", "tool.amdgpu.why", AmdGpuDrivers),
                _ => null
            };
        return null;
    }
}
