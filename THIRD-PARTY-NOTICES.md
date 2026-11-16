# Third-party components

Corvite itself is MIT-licensed (see `LICENSE`). It uses:

| Component | Licence | Use |
|---|---|---|
| CommunityToolkit.Mvvm | MIT | MVVM helpers |
| LibreHardwareMonitorLib | MPL-2.0 | temperatures, clocks, load, power sensors (unmodified library) |
| IBM Plex Sans | SIL OFL 1.1 | interface font (bundled) |
| JetBrains Mono | SIL OFL 1.1 | numbers and code font (bundled) |
| Tektur | SIL OFL 1.1 | headings font (bundled) |

NVIDIA NVML (`nvml.dll`) is not redistributed: Corvite loads it from the user's own NVIDIA driver at run time and only if it is present.
Intel XTU, AMD Ryzen Master, NVIDIA and AMD drivers are never bundled; the app only links to their official download pages.

The tables of overclocking guidelines (`ram_timings.json`, `ram_chips.json`) are summaries of public overclocker guides and are indicative only.
