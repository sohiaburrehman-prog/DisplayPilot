# DisplayPilot release verification

Tested **2026-06-21** on Windows 11, dual monitors (AORUS FO32U2P + Dell AW3423DW).

## Monitor count paths

| Setup | Verification |
|-------|----------------|
| **1 monitor** | Panel empty state, tray guidance, `SetPrimaryMonitor` / swap blocked in `DisplayManager` |
| **2 monitors** | Swap button + tray swap enabled; `SwapTest --once` swaps primary and positions OK |
| **3+ monitors** | No swap button/menu; per-monitor set-primary + arrangement map; swap API throws |

## v1.3.0 feature matrix

| Area | Check |
|------|-------|
| **Hotkeys** | Open panel (default `Ctrl+Shift+M`); rebind in Settings; optional cycle-primary hotkey; conflict warning on duplicate bindings |
| **Resolution / refresh** | Per-monitor mode list in panel; apply + validate; current mode shown on cards |
| **Auto-swap profiles** | Add/edit/remove in Settings; process match on launch; restore-on-exit; missing monitor handled gracefully |
| **Activity log** | Open from panel/tray; copy log; open log folder |
| **Update check** | Optional GitHub releases check; banner when newer version; dismiss persists |
| **Settings persistence** | `settings.json` round-trip after hotkey/profile/update changes |

## Automated checks

```powershell
dotnet run --project tools/SwapTest/SwapTest.csproj -c Release -- --once
dotnet run --project tools/MonitorLogicTest/MonitorLogicTest.csproj -c Release   # expect all passed on dual-monitor setup
dotnet build -c Release   # 0 warnings
```

## Manual smoke (5 min)

1. Launch app — panel opens, tray icon visible.
2. Set primary via card, tray menu, and arrangement map.
3. Open Settings — change a hotkey, save, confirm it works.
4. Add a test profile (e.g. `notepad.exe`), launch app, confirm swap + restore.
5. Toggle update check; confirm no network when disabled.
