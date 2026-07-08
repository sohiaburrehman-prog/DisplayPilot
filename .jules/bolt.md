## 2024-05-24 - `GetMonitors` causes micro-stutters when called on timer
**Learning:** In this project, `DisplayManager.GetMonitors()` triggers expensive Win32 GPU queries (`QueryDisplayConfig`). When invoked repeatedly in the `ProcessWatcherService` polling loop (like during profile tracking), it causes noticeable application micro-stutters.
**Action:** Always cache the target monitor information or avoid calling `GetMonitors()` on a timer/loop when tracking state that does not change frequently.
## 2024-05-24 - `QueryActiveConfig` is an expensive Win32 call
**Learning:** `QueryActiveConfig` (which wraps `QueryDisplayConfig`) is an expensive Win32 API call. When retrieving monitor information, we were making this call twice: once for friendly names and once for virtual-desktop bounds.
**Action:** Combine operations that rely on `QueryActiveConfig` into a single loop to halve the number of expensive system calls and reduce micro-stutters when `GetMonitors` is called.
## 2024-05-24 - `ProcessWatcherService` timer allocations cause GC pressure
**Learning:** In a background service polling frequently (e.g., every 1s), allocating new collections or using LINQ (like `.Where().ToList()` or `.Select().ToHashSet()`) on every tick causes unnecessary Gen0 garbage generation and pressure on the garbage collector.
**Action:** When a method is called frequently on a timer, reuse collection instances (like `HashSet` or `List`) as class-level fields and `Clear()` them on each iteration instead of allocating new ones. Avoid LINQ in hot paths.
