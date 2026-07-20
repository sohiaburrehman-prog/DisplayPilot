## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2026-07-20 - Redundant Win32 calls on idle timer ticks
**Learning:** `ProcessWatcherService.Poll()` runs on a frequent timer to reconcile game process profiles. Previously, it called `_displayManager.GetMonitors()` (which executes expensive GDI/DisplayConfig queries) unconditionally, even when the system was completely idle (no game profiles matched, and no active session to tear down).
**Action:** Always add early exits to high-frequency polling loops *before* executing native OS calls, to eliminate unneeded work when the system state is idle.
