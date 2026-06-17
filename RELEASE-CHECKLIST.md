# DisplayPilot release verification

Tested **2026-06-17** on Windows 11, dual monitors (AORUS FO32U2P + Dell AW3423DW).

| Setup | Verification |
|-------|----------------|
| **1 monitor** | Code paths: `PanelWindow` empty state, `TrayService` guidance, `SetPrimaryMonitor` / swap blocked in `DisplayManager` |
| **2 monitors** | `tools/SwapTest --once` — swap primary and positions OK; tray/panel swap branches enabled |
| **3+ monitors** | Code paths: no swap button/menu; per-monitor set-primary + arrangement map; swap API throws |

```powershell
dotnet run --project tools/SwapTest/SwapTest.csproj -c Release -- --once
dotnet run --project tools/MonitorLogicTest/MonitorLogicTest.csproj -c Release
dotnet build -c Release
```
