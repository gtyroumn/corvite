# Third-party components

Corvite itself is MIT-licensed (see `LICENSE`). It uses:

| Component | Licence | Use |
|---|---|---|
| CommunityToolkit.Mvvm | MIT | MVVM helpers |
| LibreHardwareMonitorLib | MPL-2.0 | temperatures, clocks, load, power sensors (unmodified library) |
| IBM Plex Sans | SIL OFL 1.1 | interface font (bundled) |
| JetBrains Mono | SIL OFL 1.1 | numbers and code font (bundled) |
| Tektur | SIL OFL 1.1 | headings font (bundled) |

NVIDIA NVML (`nvml.dll`) and NVAPI (`nvapi64.dll`) are not redistributed: Corvite loads them from the user's own NVIDIA driver at run time and only if they are present. The NVAPI function ids and the driver-settings structure layout used in `NvApiDrs.cs` come from NVIDIA's public NVAPI header (github.com/NVIDIA/nvapi, MIT licence, Copyright (c) NVIDIA CORPORATION & AFFILIATES); no NVAPI source is copied.

AMD's ADLX SDK is deliberately not used: its licence requires an end-user agreement with reverse-engineering prohibitions and object-code-only distribution of derivative works, which does not fit an MIT open-source project.
Intel XTU, AMD Ryzen Master, NVIDIA and AMD drivers are never bundled; the app only links to their official download pages.

The tables of overclocking guidelines (`ram_timings.json`, `ram_chips.json`) are summaries of public overclocker guides and are indicative only.
