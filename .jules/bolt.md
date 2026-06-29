## 2026-06-29 - Process Watcher Performance
**Learning:** The application calls `Process.GetProcesses()` multiple times in different places, creating unnecessary arrays of `Process` objects. Specifically, `ProfileEditorControl.xaml.cs` was calling `BuildGroupedRunningProcesses()` twice sequentially.
**Action:** Replaced double evaluation with a cached local variable and unified duplicate `GetRunningProcessNames()` methods to use `ProcessWatcherService`'s existing implementation.
