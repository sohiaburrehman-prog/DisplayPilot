# DisplayPilot Installer

Windows installer for DisplayPilot, built with [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Prerequisites

1. **.NET 8 Desktop Runtime** on the target machine (framework-dependent publish).
2. **Inno Setup 6** with `ISCC.exe` on PATH, or at:
   `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

## Build the app

From the project root (`PrimaryDisplaySwap`):

```powershell
dotnet build -c Release
dotnet publish -c Release -p:PublishProfile=FolderProfile
```

Published files land in `publish\`.

## Build the installer

From the project root:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\DisplayPilot.iss
```

Or, if `ISCC` is on PATH:

```powershell
ISCC installer\DisplayPilot.iss
```

Output: `installer\output\DisplayPilot-Setup.exe`

## Run setup

Double-click `DisplayPilot-Setup.exe`, or from PowerShell:

```powershell
Start-Process "installer\output\DisplayPilot-Setup.exe"
```

### Install location

Default: `%LOCALAPPDATA%\DisplayPilot\` (per-user, no administrator rights).

### Options during install

- **Desktop shortcut** — optional
- **Start with Windows** — optional; adds a registry Run entry with `--autostart` (tray-only launch). You can also toggle this later in the DisplayPilot tray menu.

### Upgrade

Re-run the installer over an existing install; files are overwritten in place.

### Uninstall

**Settings → Apps → Installed apps → DisplayPilot → Uninstall**, or run `%LOCALAPPDATA%\DisplayPilot\unins000.exe` if present.

## Manual deploy (without installer)

```powershell
Stop-Process -Name DisplayPilot -Force -ErrorAction SilentlyContinue
Copy-Item -Path publish\* -Destination "$env:LOCALAPPDATA\DisplayPilot\" -Recurse -Force
Start-Process "$env:LOCALAPPDATA\DisplayPilot\DisplayPilot.exe"
```
