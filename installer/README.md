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

## Portable (self-contained) build

The portable zip bundles the .NET runtime, so it runs on PCs without the .NET 8
Desktop Runtime installed.

```powershell
dotnet publish -p:PublishProfile=SelfContained
Compress-Archive -Path publish-selfcontained\* -DestinationPath DisplayPilot-Portable.zip -Force
```

Output: `publish-selfcontained\DisplayPilot.exe` (~150 MB) and
`DisplayPilot-Portable.zip`. Ship the zip as an additional release asset.

## Code signing & SmartScreen

DisplayPilot ships **unsigned by default**. Without an Authenticode certificate,
Windows SmartScreen shows a *"Windows protected your PC"* prompt the first time
the installer or app runs. Users can proceed via **More info → Run anyway**.
This is expected for new, unsigned apps and reputation improves over time.

### If you have a certificate

Signing is wired up but **never blocks the build**. To produce a signed
installer (and signed uninstaller), pass `/DSign` plus a `SignTool` definition
named `displaypilot` to ISCC:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  /DSign `
  "/Sdisplaypilot=`"C:\Windows\System32\signtool.exe`" sign /fd SHA256 /f `"C:\certs\DisplayPilot.pfx`" /p <password> /tr http://timestamp.digicert.com /td SHA256 `$f" `
  installer\DisplayPilot.iss
```

To also sign the app exe before packaging:

```powershell
signtool sign /fd SHA256 /f C:\certs\DisplayPilot.pfx /p <password> `
  /tr http://timestamp.digicert.com /td SHA256 publish\DisplayPilot.exe
```

Recommended certificate types, in order of SmartScreen friendliness:

1. **EV code-signing certificate** — clears SmartScreen immediately (hardware token / HSM required).
2. **OV (standard) code-signing certificate** — builds reputation over time.

Without `/DSign`, the build runs exactly as before and emits an unsigned
`DisplayPilot-Setup.exe`.
