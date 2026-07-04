## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
