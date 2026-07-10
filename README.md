# DisplayPilot

A lightweight Windows system-tray utility that changes which monitor is the **primary display** — instantly, with a hotkey, or automatically per app/game. Useful when games or apps always launch on the primary monitor and offer no monitor picker.

**Author:** Sohiab Rehman · **Help:** [sohiab.rehman@pm.me](mailto:sohiab.rehman@pm.me)

**Download:** [Latest release](https://github.com/sohiaburrehman-prog/DisplayPilot/releases/latest)

## Screenshots

| Flyout — Displays | Flyout — Advanced | Settings — Profiles | Setup wizard |
| --- | --- | --- | --- |
| ![Flyout Displays tab](docs/screenshots/panel-displays.png) | ![Flyout Advanced tab](docs/screenshots/panel-advanced.png) | ![Settings profiles](docs/screenshots/settings-profiles.png) | ![Setup wizard](docs/screenshots/wizard.png) |

*Placeholders ship in-repo; replace with real captures using [docs/screenshots/README.md](docs/screenshots/README.md) (Win+Shift+S).*

## Features

- **One-click primary swap** — click a monitor card or the arrangement map, or use the tray menu.
- **Tray quick actions** — swap (dual monitor), cycle primary, apply any enabled profile from the tray.
- **Custom global hotkeys** — rebind the open-panel shortcut (default `Ctrl+Shift+M`) and add an optional "cycle primary" hotkey. Captured in-app, persisted, conflict-checked.
- **Resolution & refresh switching** — pick any reported resolution/refresh per monitor from the panel; changes are validated before they're applied.
- **Per-app / per-game auto-swap profiles** — when a chosen process starts, a chosen monitor becomes primary; priorities and a selectable conflict rule decide which profile wins when apps overlap, and the previous winner is restored as the stack unwinds.
- **Headless CLI** — list monitors/profiles/presets, apply profiles or layouts, set primary/HDR/projection, and export/import settings. Add `--json` for stable scripting output (`DisplayPilot.exe --help`).
- **What's new** — dismissible release-notes banner after upgrades; optional GitHub update check.
- **Activity log viewer** — view, copy, or open the log folder in-app. Unhandled exceptions are recorded for diagnosis.
- **Telemetry-free update check** — optional startup check against the GitHub releases API (no downloads, no analytics) with a dismissible banner.
- **Starts with Windows** — optional, launches hidden to the tray.

## How it works

Windows treats the monitor whose desktop origin is at **(0, 0)** as the primary display. DisplayPilot tries two mechanisms:

1. **DisplayConfig** (`QueryDisplayConfig` / `SetDisplayConfig`) — the same API Windows Settings uses. Shifts all monitor positions so the chosen monitor lands at (0, 0).
2. **ChangeDisplaySettingsEx** with `CDS_SET_PRIMARY` — classic staged GDI fallback when DisplayConfig is rejected by the display driver.

Resolution/refresh changes use `EnumDisplaySettings` (enumerate) + `ChangeDisplaySettingsEx` with `CDS_TEST` (validate) then `CDS_UPDATEREGISTRY` (apply). Monitor friendly names come from `DisplayConfigGetDeviceInfo`.

## Requirements

- Windows 10 or 11 (64-bit)
- For the **framework-dependent** build: [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)
- The **self-contained / portable** build bundles the runtime — no install required.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — only to build from source

Check installed runtimes:

```powershell
dotnet --list-runtimes
```

Look for `Microsoft.WindowsDesktop.App 8.x`.

## Install

| Method | Command / link |
| --- | --- |
| **GitHub (recommended)** | [DisplayPilot-Setup.exe](https://github.com/sohiaburrehman-prog/DisplayPilot/releases/latest) — per-user, no admin |
| **Portable** | [DisplayPilot-Portable.zip](https://github.com/sohiaburrehman-prog/DisplayPilot/releases/latest) — self-contained, unzip and run |
| **winget (community)** | `winget install SohiabRehman.DisplayPilot` after [winget-pkgs PR](packaging/winget/README.md); test locally with `winget install --manifest packaging\winget\SohiabRehman.DisplayPilot\1.7.0` |

## Download options

| Asset | Runtime needed | Size | Notes |
| --- | --- | --- | --- |
| `DisplayPilot-Setup.exe` | .NET 8 Desktop Runtime | ~5 MB | Recommended installer (per-user, no admin). |
| `DisplayPilot-Portable.zip` | None (bundled) | ~150 MB | Self-contained single exe; unzip and run. |

## Command-line interface

Headless mode (no tray/GUI):

```powershell
DisplayPilot.exe --help
DisplayPilot.exe --list-monitors
DisplayPilot.exe --list-profiles --json
DisplayPilot.exe --set-primary 1
DisplayPilot.exe --set-primary \\.\DISPLAY2
DisplayPilot.exe --set-projection extend
DisplayPilot.exe --apply-profile <name-or-id> --json
DisplayPilot.exe --apply-preset <name-or-id> --json
DisplayPilot.exe --export-settings backup.json
DisplayPilot.exe --import-settings backup.json
```

## Build (development)

```powershell
# From the repo root (DisplayPilot/)
dotnet build -c Release
dotnet run -c Release
```

## Publish

Framework-dependent (primary build — small exe, fast startup):

```powershell
dotnet publish -p:PublishProfile=FolderProfile
```

**Output:** `publish\DisplayPilot.exe` plus `DisplayPilot.dll`.

Self-contained single file (for the portable zip):

```powershell
dotnet publish -p:PublishProfile=SelfContained
Compress-Archive -Path publish-selfcontained\* -DestinationPath DisplayPilot-Portable.zip -Force
```

**Output:** `publish-selfcontained\DisplayPilot.exe` (no runtime needed).

## Installer

See [installer/README.md](installer/README.md) for building `DisplayPilot-Setup.exe` with Inno Setup and for the **code-signing / SmartScreen** notes.

## Usage

- **Flyout panel** — opens on first launch and via the hotkey. Set primary, swap, change resolution/refresh, open Settings or the activity log.
- **Settings window** — rebind hotkeys, manage auto-swap profiles, toggle the update check.
- **Tray icon** — right-click for displays, **QUICK ACTIONS** (swap, cycle, apply profile), Settings, log, and Exit; double-click to open the panel.
- **`Ctrl+Shift+M`** (default) — open the panel from anywhere.
- **X button** — hides to tray (does not exit). Use **Exit** in the tray menu to quit.

Files in `%LOCALAPPDATA%\DisplayPilot\`:

- `log.txt` — current session log (`log.prev.txt` is the previous session)
- `settings.json` — hotkeys, profiles, and update preferences

## Project structure

```
DisplayPilot/                      # repo root
├── Assets/AppIcon.ico
├── CHANGELOG.md
├── Native/DisplayInterop.cs     # P/Invoke surface
├── Services/
│   ├── DisplayManager.cs        # enumerate, set primary, modes
│   ├── SettingsService.cs       # JSON settings store
│   ├── HotkeyService.cs         # global hotkey registration
│   ├── ProcessWatcherService.cs # per-app auto-swap watcher
│   ├── ProfileMatcher.cs        # pure profile-resolution logic (tested)
│   ├── CliCommands.cs           # headless CLI
│   ├── ChangelogService.cs      # release notes
│   ├── UpdateService.cs         # GitHub release check
│   ├── StartupService.cs        # run-at-startup registry entry
│   └── TrayService.cs           # tray icon + menu
├── packaging/winget/            # winget community manifests
├── docs/screenshots/            # README screenshots (+ capture guide)
├── Models/                      # MonitorInfo, DisplayMode, AppSettings
├── PanelWindow.xaml(.cs)        # main flyout
├── SettingsWindow.xaml(.cs)     # hotkeys / profiles / updates
├── LogViewerWindow.xaml(.cs)    # in-app log viewer
├── Themes/Theme.xaml            # dark indigo theme
├── installer/DisplayPilot.iss   # Inno Setup script
└── tools/                       # MonitorLogicTest, SwapTest, CreateAppIcon
```

## Testing

```powershell
dotnet run --project tools\MonitorLogicTest -c Release    # logic suite (branch, modes, profiles, hotkeys, settings, updates)
dotnet run --project tools\SwapTest -c Release -- --modes # dry-run: dump available modes per monitor
```

## License

| Scope | Terms |
| --- | --- |
| **Source code** (this repository) | [MIT](LICENSE) — Copyright (c) Sohiab Rehman |
| **Installed application** (releases, installer, portable zip) | Proprietary [End User License Agreement](docs/legal/EULA.txt) |
| **Third-party components** | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) |

## Stack

C# / .NET 8 — WPF flyout + WinForms `NotifyIcon` tray, with DisplayConfig + `CDS_SET_PRIMARY` fallback for reliable primary-monitor switching on Windows 10/11.

See [docs/LANGUAGE-OPTIONS.md](docs/LANGUAGE-OPTIONS.md) for language/stack rewrite options.
