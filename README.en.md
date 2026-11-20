# Corvite

[Русский](README.md) · [Українська](README.uk.md) · [English](README.en.md)

A Windows 11 optimizer for games: safe tweaks for the system, network, memory and background services, profiles with a risk estimate, RAM timing guidelines and a local advisor, Corvi. Everything the app changes can be rolled back. MIT licence.

## Install
1. Download `Corvite-Setup.exe` (or the portable zip) from the [Releases](https://github.com/gtyroumn/corvite/releases) page and run it. Requires Windows 11 x64 and administrator rights.
2. Windows SmartScreen may say "Windows protected your PC" and "unknown publisher": the project has no paid code-signing certificate. Click "More info", then "Run anyway".
3. On first launch choose PC or laptop. The app saves a backup of your original settings before making any change.

## Good to know
- Changes are only marked first and applied with the "Apply" button. The red zone asks for confirmation.
- You change system settings at your own risk. Overclock parameters can raise temperatures and cause crashes; limits are tighter on laptops. FPS gains are estimates.
- The app does not install drivers or bypass system protection. CPU limits and voltage, and some GPU settings, are not written by the app: tuning guides with values and a button that opens the official program cover them (see "Tuning guides").

## Rollback and uninstall
- "Discard changes" (bottom bar) resets what is not applied, "Restore previous" reverts the last apply.
- Overview, "Restore original settings" returns everything to the first-launch state. The backup is in `%ProgramData%\Corvite\Backups`.
- Uninstall via Settings, Apps: the uninstall window offers to restore the original settings. The `%ProgramData%\Corvite` folder (backup, log, settings) stays: delete it by hand if you do not need it.

## Driver settings and tuning guides
- **NVIDIA:** power limit and clock offsets go through NVML; the driver switches (maximum performance power mode, low latency, shader cache) go through NVAPI (`nvapi64.dll`, NVIDIA's public MIT-licensed header). These are the same global settings as in the NVIDIA Control Panel; they persist after a reboot and can be rolled back from the history.
- **Not written by the app:** CPU power limits, boost, Curve Optimizer, voltage (a kernel driver would be needed and anti-cheat may object), GPU temperature target, Reflex, AMD Anti-Lag and all AMD GPU tweaks. Those rows are removed; guides replace them.
- **Tuning guides** on the Processor and Graphics pages: pick a socket or series, then a model (172 CPUs: Ryzen 1000 and newer, Intel 9th gen and newer; 69 desktop GPUs: GeForce GTX 1060 and newer, Radeon RX 570 / Vega and newer). Lists follow the device type. You get a Safe / Game / Max table, where to change each value, step-by-step instructions, and a button that opens the installed official program (or its download page). Values are rule-based references, not measurements.
- History of applies (header button) rolls back to any step; Export / Import on the profiles tab moves settings between PCs.
