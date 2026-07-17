# DisplayPilot release verification

Tested **2026-07-14** on Windows 11, dual monitors (AORUS FO32U2P + Dell AW3423DW).

## Monitor count paths

| Setup | Verification |
|-------|----------------|
| **1 monitor** | Panel empty state, tray guidance, `SetPrimaryMonitor` / swap blocked in `DisplayManager` |
| **2 monitors** | Swap button + tray swap enabled; `SwapTest --once` swaps primary and positions OK |
| **3+ monitors** | No swap button/menu; per-monitor set-primary + arrangement map; swap API throws |

## Feature matrix

| Area | Check |
|------|-------|
| **Hotkeys** | Open panel (default `Ctrl+Shift+M`); rebind in Settings; optional cycle-primary hotkey; conflict warning on duplicate bindings |
| **Resolution / refresh** | Per-monitor mode list in panel; apply + validate; current mode shown on cards |
| **Auto-swap profiles** | Add/edit/remove in Settings; process match on launch; restore-on-exit; missing monitor handled gracefully |
| **Activity log** | Open from panel/tray; copy log; open log folder |
| **Update check** | Optional GitHub releases check; banner when newer version; dismiss persists |
| **Settings persistence** | `settings.json` round-trip after hotkey/profile/update changes |
| **Display scenes** | Capture and preview full topology; apply and keep; timeout and explicit revert; missing display preflight; failed apply rollback |
| **Scene profiles** | Profile applies full scene; higher-priority scene takes over; lower profile resumes; original full scene restores after final exit |
| **Scene CLI** | Capture/rename/delete/export/import; referenced-scene deletion blocked; `--temporary` restores; JSON success and error envelopes parse |

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
6. Resize Settings and Profiles near the work-area minimum; confirm every page and editor remains reachable by keyboard and scrolling.
7. Start two matching profile processes; confirm the winner shows “Controlling display” and the lower profile shows “Matched · waiting.”
8. Apply a scene and let the 15-second confirmation expire; confirm resolution, position, orientation, primary, and HDR all restore.
9. Repeat scene apply and choose both “Keep changes” and “Revert now.”
10. Test scenes across mixed resolution/refresh, rotated display, HDR on/off, and a scene whose saved display is disconnected.
11. Assign scenes to two overlapping profiles; close the winner, then the remaining profile; confirm stack transition and final full-scene restoration.
