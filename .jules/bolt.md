## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2025-02-18 - Early return during idle profiling polling loops
**Learning:** Polling loops such as `ProcessWatcherService.ReconcileWinner` can trigger expensive OS-level operations (like `GetMonitors()`) repeatedly even when no active profiles are engaged and there's no active session to tear down (true idle).
**Action:** Always add early returns for 'true idle' states (e.g. `preferred is null && _winnerSnapshot is null`) to skip unnecessary processing and Win32 queries to reduce micro-stutters and CPU overhead.
