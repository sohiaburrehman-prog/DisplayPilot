## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-05-24 - High-frequency polling loop LINQ allocations
**Learning:** In C#, inline LINQ operations (e.g. `Select`, `ToHashSet`, `Where`, `ToList`) inside a high-frequency background polling loop like `ProcessWatcherService.Poll()` allocate new memory for enumerators, closures, and collections every poll interval. This generates continuous Gen0 Garbage Collection pressure, which can cause micro-stutters in a desktop application.
**Action:** Replace inline LINQ and local collection instantiation in polling loops with reused class-level collections (e.g. `HashSet`, `List`) and call `.Clear()` before repopulating them.
