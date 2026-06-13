# DisplayPilot

A lightweight Windows system-tray utility that lets you quickly change which monitor is the **primary display**. Useful when games or apps always launch on the primary monitor and do not offer a monitor picker.

**Author:** Sohiab Rehman · **Help:** [sohiab.rehman@pm.me](mailto:sohiab.rehman@pm.me)

> **Note:** The source folder is still named `PrimaryDisplaySwap`; the product name is **DisplayPilot**.

## How it works

Windows treats the monitor whose desktop origin is at **(0, 0)** as the primary display. This app tries two mechanisms:

1. **DisplayConfig** (`QueryDisplayConfig` / `SetDisplayConfig`) — the same API Windows Settings uses. Shifts all monitor positions so the chosen monitor lands at (0, 0).
2. **ChangeDisplaySettingsEx** with `CDS_SET_PRIMARY` — classic staged GDI fallback when DisplayConfig is rejected by the display driver.

Monitor friendly names come from `DisplayConfigGetDeviceInfo`.

## Requirements

- Windows 10 or 11 (64-bit for the published build)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64) — required for the published build
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — only needed to build from source

Check installed runtimes:

```powershell
dotnet --list-runtimes
```

Look for `Microsoft.WindowsDesktop.App 8.x`.

## Build (development)

```powershell
cd "C:\Users\SOF\Documents\Personal Documents\PrimaryDisplaySwap"
dotnet build -c Release
dotnet run -c Release
```

## Publish (recommended)

Framework-dependent publish — small exe, fast startup (requires .NET 8 Desktop Runtime on the PC):

```powershell
dotnet publish -p:PublishProfile=FolderProfile
```

**Output:** `publish\DisplayPilot.exe` plus `DisplayPilot.dll` (~5 MB total vs ~170 MB self-contained).

### Self-contained fallback (optional)

If the target PC does not have .NET 8 Desktop Runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-selfcontained
```

## Install

```powershell
$installDir = "$env:LOCALAPPDATA\DisplayPilot"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item .\publish\* $installDir\ -Force
& "$installDir\DisplayPilot.exe"
```

Enable **Start with Windows** from the mini panel or tray menu after installing to the final location.

## Usage

- **Mini panel** — main UI; opens on startup. Select a monitor and click **Set Primary**, or **Swap 1 ↔ 2** with two monitors.
- **Tray icon** — right-click for quick monitor list; double-click to restore the panel.
- **Ctrl+Shift+M** — restore the panel when hidden.
- **X button** — hides to tray (does not exit). Use **Exit** in the tray menu to quit.
- Monitor list refreshes automatically when displays are plugged/unplugged.

Log file: `%LOCALAPPDATA%\DisplayPilot\log.txt`

## Project structure

```
PrimaryDisplaySwap/          # repo folder (legacy name)
├── Assets/AppIcon.ico
├── Native/DisplayInterop.cs
├── Services/DisplayManager.cs
├── Services/StartupService.cs
├── Models/MonitorInfo.cs
├── TrayHostForm.cs          # Tray icon, hotkey, display-change host
├── MiniControlForm.cs       # Dark-themed control panel
├── AppTheme.cs
├── docs/LANGUAGE-OPTIONS.md # Rewrite stack comparison
└── Program.cs
```

## Stack

C# / .NET 8 WinForms — minimal tray app with DisplayConfig + CDS_SET_PRIMARY fallback for reliable primary-monitor switching on Windows 10/11.

See [docs/LANGUAGE-OPTIONS.md](docs/LANGUAGE-OPTIONS.md) for language/stack rewrite options.
