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
- The app does not install drivers or bypass system protection. Parameters that need a vendor program (Intel XTU, Ryzen Master, NVIDIA driver) are locked and point to the official site.

## Rollback and uninstall
- "Discard changes" (bottom bar) resets what is not applied, "Undo" reverts the last apply.
- Overview, "Restore original settings" returns everything to the first-launch state. The backup is in `%ProgramData%\Corvite\Backups`.
- Uninstall via Settings, Apps: the uninstall window offers to restore the original settings. The `%ProgramData%\Corvite` folder (backup, log, settings) stays: delete it by hand if you do not need it.
