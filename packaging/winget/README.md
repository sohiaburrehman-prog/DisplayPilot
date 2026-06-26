# DisplayPilot — winget packaging

Manifests for submitting DisplayPilot to [winget-pkgs](https://github.com/microsoft/winget-pkgs).

## Package identifier

`SohiabRehman.DisplayPilot`

## Install from local manifest (test before PR)

From the repo root, after updating the installer SHA256 in the version manifest:

```powershell
winget install --manifest "packaging\winget\SohiabRehman.DisplayPilot\1.5.0"
```

Or point at the version folder:

```powershell
winget install --manifest "packaging\winget\SohiabRehman.DisplayPilot"
```

## Submit to winget-pkgs

1. Build `DisplayPilot-Setup.exe` and publish GitHub release **v1.5.0** (or newer).
2. Compute SHA256 of the installer:
   ```powershell
   Get-FileHash .\DisplayPilot-Setup.exe -Algorithm SHA256
   ```
3. Update `1.5.0/SohiabRehman.DisplayPilot.installer.yaml` with the hash and verify `InstallerUrl` matches the release asset URL.
4. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).
5. Copy `packaging/winget/SohiabRehman.DisplayPilot/` into `manifests/s/So/SohiabRehman.DisplayPilot/` in your fork.
6. Open a PR using the winget submission template; CI validates manifests.

After merge, users can run:

```powershell
winget install SohiabRehman.DisplayPilot
```

## Manifest layout

```
SohiabRehman.DisplayPilot/
├── SohiabRehman.DisplayPilot.yaml          # version list
└── 1.5.0/
    ├── SohiabRehman.DisplayPilot.yaml
    ├── SohiabRehman.DisplayPilot.installer.yaml
    └── SohiabRehman.DisplayPilot.locale.en-US.yaml
```

## Notes

- Installer type: **inno** (Inno Setup per-user install).
- Scope: **user** (installs to `%LOCALAPPDATA%\DisplayPilot`).
- Requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) x64 for the setup exe (framework-dependent build).
