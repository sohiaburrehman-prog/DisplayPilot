## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-07-11 - Fast-path process reconciliation
**Learning:** `ReconcileWinner` in `ProcessWatcherService` runs on every polling interval and previously invoked `DisplayManager.GetMonitors()` (which ultimately queries hardware devices via expensive Win32 APIs) regardless of whether an active profile or past session exists.
**Action:** When working on timers or continuous background checks that invoke system queries, always implement early fast-paths to bypass the queries if there are no state changes needed.
