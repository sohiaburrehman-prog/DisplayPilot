1. Refactor ProcessPickerHelper to use the shared `ProcessWatcherService.GetRunningProcessNames` instead of allocating and iterating `Process.GetProcesses()` on its own. `ProcessWatcherService` already provides a public method that does exactly this, which returns a `HashSet<string>` ignoring case.
2. In `ProcessPickerHelper.cs`, remove `GetRunningProcessNames()` and change all calls to `ProcessWatcherService.GetRunningProcessNames()`.
3. Pre-commit check to ensure everything builds correctly.
