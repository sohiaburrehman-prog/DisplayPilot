## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-05-24 - Gen0 GC pressure in high-frequency polling loop
**Learning:** In C#, using LINQ extensions like `.ToList()` and `.ToHashSet()` or allocating new collections inside a high-frequency polling loop (like `ProcessWatcherService.Poll()`) creates continuous Gen0 garbage collection pressure. This forces the runtime to pause execution frequently to clean up short-lived objects.
**Action:** When a method is called frequently (e.g. every second by a timer), replace local object and collection allocations with class-level fields. Call `.Clear()` on them at the start or end of the method block to reuse the memory allocation. Avoid LINQ in hot paths in favor of traditional loops to further reduce hidden allocations.
