# Corvite

[Русский](README.md) · [Українська](README.uk.md) · [English](README.en.md)

Corvite tunes Windows 11 for games. It shows what can be improved in the system, network, memory, background services, CPU and graphics card, rates the risk of every change and applies only what you marked. Everything it changes can be rolled back. For CPU, RAM and GPU overclocking it has reference guides with values for your exact model. MIT licence; the interface is in Russian, Ukrainian and English.

## Installation
1. Download `Corvite-Setup.exe` (or the portable zip) from [Releases](https://github.com/gtyroumn/corvite/releases) and run it. Windows 11 x64 and administrator rights are required.
2. SmartScreen may say "Windows protected your PC" and "unknown publisher": the project has no paid code-signing certificate. Click "More info", then "Run anyway".
3. In the installer choose the folder, the program language and whether you want a desktop shortcut. The installed program appears in Settings → Apps.

The portable zip only needs to be unpacked; run `Corvite.exe` as administrator.

## First launch
The program asks whether this is a PC or a laptop. Limits depend on it: on a laptop the overclocking sliders are halved, because cooling and power are weaker. You can change the choice later.

Before changing anything, Corvite saves the original values of every parameter to `%ProgramData%\Corvite\Backups\baseline.json`. A Windows restore point is also created before the first apply in a session (Windows allows one per day, so if it fails the program simply continues).

## How to use
Tabs are on the left; the bar at the bottom shows the number of changes and the buttons.

1. Open a tab and mark the settings you want. Changes are only remembered and highlighted; nothing happens in the system yet. Every setting has a hint (the i icon) explaining what it does and how it affects latency, stability and heat.
2. Overclocking sliders have colour zones. Green is safe, yellow needs good cooling, red is available only after confirmation.
3. Press "Apply". Settings that need a restart are marked.
4. If you change your mind before applying, "Discard changes" clears what is marked.

Tabs:
- **Overview.** System status, four ready profiles (Stock, Safe, Game, Max), overall risk and an estimated FPS gain (an estimate, not a measurement), and the Protection block.
- **Clocks.** Your CPU, graphics card, memory and board, live temperatures, load and clocks.
- **Windows.** Game Mode, background recording, GPU scheduling, fullscreen optimisations, timer, fast startup and other system settings.
- **Processor.** Power and cores, plus the CPU overclocking guide.
- **Graphics.** Power limit and clocks, driver settings, the GPU guide.
- **Memory and disk.** Standby-memory cleaning, temp-file cleaning before a game, SysMain, TRIM, indexing.
- **RAM timings.** Reference for tuning memory.
- **Network.** TCP, Nagle, DNS.
- **Background.** Closing background programs, priorities, background load limit, pausing updates and notifications during a game.

Some rows are grey and say "unavailable": the setting does not exist on your hardware, for example GPU overclocking on a laptop whose driver blocks it.

## Profiles
A profile sets a whole group of values at once. Stock returns Windows and driver defaults, Safe enables only proven tweaks without overclocking, Game looks for a balance of response, FPS and temperature, Max goes to the limit of your cooling including the red zone. After picking a profile you can adjust it by hand.

Next to the profiles on Overview there are **Export** and **Import** buttons: settings are saved to one file that you can carry to another PC. Imported values appear as not applied, so you decide whether to apply them.

## Rollback
- "Discard changes" clears what is marked but not yet applied.
- **Apply history** (button in the header) keeps every step of the session. You can go back to any of them, not just one step.
- "Restore original settings" (Overview) returns everything to the state of the first launch from the copy in `%ProgramData%\Corvite\Backups`.
- When uninstalling, the uninstall window offers to restore the original Windows settings first. The `%ProgramData%\Corvite` folder (backup, log, settings) can be deleted by hand.

The log is at `%ProgramData%\Corvite\Logs\corvite.log`; attach it if something goes wrong.

## Protection
A block on Overview. If after an apply the CPU or GPU temperature stays above the threshold for two measurements in a row within 15 minutes, the last apply is cancelled automatically. The threshold and the switches are kept between launches.

## During a game
A game is recognised by its fullscreen window. With the matching switches on, for that time the program raises the game's priority, closes background programs from a list, limits background CPU load, pauses Windows updates and indexing, silences notifications and cleans standby memory. Afterwards everything is put back. The list of programs to close is in `%ProgramData%\Corvite\kill-list.txt` (created on the first game start, one process name per line, `#` disables a line). If Corvite closes unexpectedly during a game, the next launch restores services and notifications by itself.

## Corvi, the advisor
The "Corvi" button in the header opens a chat. The advisor works on rules and does not send your data anywhere. It can:
- choose settings for a specific game or genre (shooters, MOBA, third-person): values are set as not applied, so you review and apply them yourself;
- explain any setting and say how far it is safe to raise it;
- tell you where to find something and jump to the right tab;
- show what you have already changed and explain profile risks;
- add a game by name.

It answers in the language of the question. The game list is refreshed from GitHub at start without a new app release.

## Overclocking: what the program does and what it advises
Corvite writes to hardware only what can be done safely without a kernel driver.

| Hardware | What happens |
|---|---|
| NVIDIA GeForce GTX 1060 and newer | The program changes the power limit and core / memory clock offsets itself (NVML) and driver settings: power mode, low latency, shader cache (NVAPI, as in the NVIDIA Control Panel). A guide covers the rest. |
| AMD Radeon RX 570 / Vega and newer | Guide and a button that opens AMD Software. |
| AMD Ryzen 1000 and newer, Intel 9th gen and newer | Guide and a button that opens Ryzen Master or Intel XTU. |
| Intel graphics | Not supported. |

Why processors are guide-only: writing power limits, multiplier and voltage needs a kernel driver with a paid signature, and many anti-cheat systems react to such drivers. The program does not do this.

### Guides
The Processor and Graphics tabs have a collapsible card. Pick a socket (a series for graphics cards), then a model. The lists follow the device type: on a PC you will not see laptop processors. The installed model is detected automatically.

For the model you get:
- specifications (cores, clocks, TDP, memory size and type);
- a Safe / Game / Max table with concrete values;
- where to change them: BIOS, Ryzen Master, Intel XTU, NVIDIA Control Panel, AMD Software, MSI Afterburner;
- step-by-step instructions and notes (for example X3D or Intel 13th/14th gen specifics);
- a button that opens the installed official program, or the manufacturer's page if it is not installed. Only intel.com, amd.com and nvidia.com addresses are opened.

The database has 172 processors (`Data/cpus.json`) and 69 desktop graphics cards (`Data/gpus.json`). Values are calculated from specifications (TDP, PPT, Tjmax, board power) and rounded conservatively: a reference, not measurements of your sample. Start with the Safe column, change one setting at a time and check stability and temperature after every step.

### RAM timings
The "RAM timings" tab shows a table for DDR4 and DDR5 by your processor and the BIOS path to the settings. You can pick the memory chip from the database (17 revisions of Samsung, SK hynix and Micron) or rely on the part-number hint. Nothing is applied: timings are entered in BIOS. To identify the chip there is a link to Thaiphoon Burner on a Softpedia catalogue page, since the author's site is gone. Values are collected from public overclocking guides ([ozgk/ddr4-oc-guide](https://github.com/ozgk/ddr4-oc-guide), webpcdesigns [DDR4](https://webpcdesigns.com/blog/ddr4-overclocking-guide) and [DDR5](https://webpcdesigns.com/blog/ddr5-overclocking-guide)); sources sometimes disagree, so some chips carry a "little data" label.

## Sensors
Temperatures, clocks and load come from LibreHardwareMonitor. On some systems Windows or an antivirus blocks its CPU access driver: the CPU temperature then comes from the ACPI thermal zone (missing on some laptops) or shows "—". Load and memory work without the driver.

## Updates
At start the program asks GitHub once for the latest release and, if it is newer, shows a banner with a link on Overview. Turn it off in `%ProgramData%\Corvite\config.json`: `"CheckUpdates": false`. Updating is manual: download the new `Corvite-Setup.exe` and run it.

## Command line
- `--dry-run`: show the interface and calculate everything, but write nothing to the system.
- `--demo`: check the look on demo hardware configurations (6 of them, chosen on the Clocks tab).
- `--restore-baseline`: quietly return all settings to the first-launch values.
- `--uninstall`: the uninstall window (also what the Apps entry runs).
- `--install --dry-run`, `--uninstall --dry-run`: view the installer and uninstaller without writing anything.

## Building from source
Windows 11 and the .NET 8 SDK are required.
```
dotnet build Corvite.sln -c Release
```
Run (administrator rights are required; plain `dotnet run` from a normal console fails with error 740):
```
Start-Process "src\Corvite.App\bin\Release\net8.0-windows10.0.22000.0\Corvite.exe" -Verb RunAs
```
Releases are built by GitHub Actions: a tag like `v1.0` starts the `release` workflow, which publishes `Corvite-Setup.exe` and the portable zip. Every push is built by the `build` workflow.

## Structure
- `src/Corvite.Core`: parameter catalogue, profile and risk calculation, the Corvi advisor, data (`Data/*.json`: games, RAM timings and chips, processors, graphics cards, demo hardware).
- `src/Corvite.Hardware`: hardware detection (WMI) and sensors (LibreHardwareMonitor).
- `src/Corvite.Tweaks`: the changes themselves (registry, services, network, NVML and NVAPI), the original-values copy, rollback, game mode.
- `src/Corvite.App`: the WPF application, theme, localisation (`Resources/Strings*.resx`), installer.

Third-party components and their licences are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Good to know
You change system settings at your own risk. Overclocking raises temperatures and can cause crashes. The FPS gain estimate is approximate. The program does not install drivers, does not bypass system protection and downloads nothing without your knowledge.
