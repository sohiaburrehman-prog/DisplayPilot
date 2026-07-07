## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2026-07-07 - `GetMonitors` caching optimization
**Learning:** Found redundant `GetMonitors()` calls in the same method loop chain, causing expensive Win32 OS-level API queries multiple times unnecessarily when auto-swapping monitors during profiling.
**Action:** Always thread a nullable cache of monitors through methods that check monitor state sequentially to save redundant Win32 queries, invalidating it immediately whenever state is actually mutated (e.g. `SetPrimaryMonitor`).
