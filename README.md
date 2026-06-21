# DisplayPilot

A lightweight Windows system-tray utility that changes which monitor is the **primary display** — instantly, with a hotkey, or automatically per app/game. Useful when games or apps always launch on the primary monitor and offer no monitor picker.

**Author:** Sohiab Rehman · **Help:** [sohiab.rehman@pm.me](mailto:sohiab.rehman@pm.me)

**Download:** [Latest release](https://github.com/sohiaburrehman-prog/DisplayPilot/releases/latest)

> **Note:** The source folder is still named `PrimaryDisplaySwap`; the product name is **DisplayPilot**.

## Screenshots

| Flyout panel | Settings | Tray menu |
| --- | --- | --- |
| `docs/screenshots/panel.png` | `docs/screenshots/settings.png` | `docs/screenshots/tray.png` |

*(PNG files are not committed yet — add them before a public release; see below.)*

### Capturing screenshots

Use **Win+Shift+S** (Snipping Tool) to capture each view, then save PNGs with these exact names in `docs/screenshots/`:

| Filename | What to capture |
| --- | --- |
| `panel.png` | Flyout panel open — monitor cards, arrangement map, resolution dropdowns |
| `settings.png` | Settings window — hotkeys, auto-swap profiles, update check toggle |
| `tray.png` | Tray right-click menu — monitor list, Settings, log, Exit |

Optional fourth shot: `profiles.png` (auto-swap profile editor with a sample rule). After saving, the table above will render the images on GitHub automatically.

## Features

- **One-click primary swap** — click a monitor card or the arrangement map, or use the tray menu.
- **Custom global hotkeys** — rebind the open-panel shortcut (default `Ctrl+Shift+M`) and add an optional "cycle primary" hotkey. Captured in-app, persisted, conflict-checked.
- **Resolution & refresh switching** — pick any reported resolution/refresh per monitor from the panel; changes are validated before they're applied.
- **Per-app / per-game auto-swap profiles** — when a chosen process starts, a chosen monitor becomes primary; optionally restore the previous primary on exit. Robust to monitors that aren't connected.
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

## Download options

| Asset | Runtime needed | Size | Notes |
| --- | --- | --- | --- |
| `DisplayPilot-Setup.exe` | .NET 8 Desktop Runtime | ~5 MB | Recommended installer (per-user, no admin). |
| `DisplayPilot-Portable.zip` | None (bundled) | ~150 MB | Self-contained single exe; unzip and run. |

## Build (development)

```powershell
cd "C:\Users\SOF\Documents\Personal Documents\PrimaryDisplaySwap"
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
- **Tray icon** — right-click for quick monitor list, cycle primary, Settings, log, and Exit; double-click to open the panel.
- **`Ctrl+Shift+M`** (default) — open the panel from anywhere.
- **X button** — hides to tray (does not exit). Use **Exit** in the tray menu to quit.

Files in `%LOCALAPPDATA%\DisplayPilot\`:

- `log.txt` — current session log (`log.prev.txt` is the previous session)
- `settings.json` — hotkeys, profiles, and update preferences

## Project structure

```
PrimaryDisplaySwap/              # repo folder (legacy name; product = DisplayPilot)
├── Assets/AppIcon.ico
├── Native/DisplayInterop.cs     # P/Invoke surface
├── Services/
│   ├── DisplayManager.cs        # enumerate, set primary, modes
│   ├── SettingsService.cs       # JSON settings store
│   ├── HotkeyService.cs         # global hotkey registration
│   ├── ProcessWatcherService.cs # per-app auto-swap watcher
│   ├── ProfileMatcher.cs        # pure profile-resolution logic (tested)
│   ├── UpdateService.cs         # GitHub release check
│   ├── StartupService.cs        # run-at-startup registry entry
│   └── TrayService.cs           # tray icon + menu
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

## Stack

C# / .NET 8 — WPF flyout + WinForms `NotifyIcon` tray, with DisplayConfig + `CDS_SET_PRIMARY` fallback for reliable primary-monitor switching on Windows 10/11.

See [docs/LANGUAGE-OPTIONS.md](docs/LANGUAGE-OPTIONS.md) for language/stack rewrite options.
