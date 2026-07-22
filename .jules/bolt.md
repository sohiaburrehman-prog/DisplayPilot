## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-07-22 - Fast-path polling loop when idle
**Learning:** Polling loops in desktop applications (like `ProcessWatcherService`) that run repeatedly can cause micro-stutters if they do not fast-path or early-return when idle. Bypassing expensive object instantiation or Win32 calls (such as `GetMonitors()`) in a polling loop when the application state implies no action is necessary greatly improves background efficiency.
**Action:** When working on polling mechanisms, always ensure there is a cheap check to early-return before hitting heavier operations or native OS calls when there's no work to be done.
