## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-10-27 - Process watcher idle state micro-stutters
**Learning:** Even with combined `QueryActiveConfig` calls, calling `DisplayManager.GetMonitors()` in a polling loop causes micro-stutters. In `ProcessWatcherService`, it was being called every 1 second when no profiles were active, just to verify no display changes were needed.
**Action:** Add fast paths/early returns in polling loops during idle states to avoid calling `GetMonitors()` (and its underlying Win32 queries) when no action is required.
