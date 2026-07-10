using System.Diagnostics;

namespace PrimaryDisplaySwap.Services;

/// <summary>Builds grouped process lists for profile and wizard pickers.</summary>
public static class ProcessPickerHelper
{
    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "sihost", "taskhostw", "runtimebroker", "searchhost",
        "explorer", "dllhost", "conhost", "audiodg", "spoolsv", "ctfmon", "winlogon",
        "securityhealthservice", "searchindexer", "msedgewebview2", "gamebarpresencewriter",
        "steamwebhelper", "epicwebhelper", "eabackgroundservice", "originwebhelperservice",
        "ubisoftconnectwebcore", "galaxycommunication", "crashpad_handler",
        "cefsharp.browsersubprocess", "unitycrashhandler32", "unitycrashhandler64",
    };

    public sealed class ProcessGroup
    {
        public required string Header { get; init; }
        public required IReadOnlyList<string> Items { get; init; }
    }

    public sealed class RecentProcess
    {
        public required string ProcessName { get; init; }
        public required string ExeLabel { get; init; }
        public DateTime StartedUtc { get; init; }
    }

    public static bool IsExcludedProcess(string processNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(processNameWithoutExtension))
        {
            return true;
        }

        if (ExcludedProcesses.Contains(processNameWithoutExtension))
        {
            return true;
        }

        return LauncherCatalog.IsKnownLauncher(processNameWithoutExtension + ".exe");
    }

    public static IReadOnlyList<RecentProcess> GetRecentlyStartedProcesses(int withinMinutes = 15)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-withinMinutes);
        var results = new List<RecentProcess>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (IsExcludedProcess(name) || !seen.Add(name))
                {
                    continue;
                }

                var started = process.StartTime.ToUniversalTime();
                if (started < cutoff)
                {
                    continue;
                }

                results.Add(new RecentProcess
                {
                    ProcessName = name,
                    ExeLabel = name + ".exe",
                    StartedUtc = started,
                });
            }
            catch
            {
                // Access denied or process exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results
            .OrderByDescending(p => p.StartedUtc)
            .ToList();
    }

    public static IReadOnlyList<string> GetRecentlyStartedExeLabels(int withinMinutes = 15) =>
        GetRecentlyStartedProcesses(withinMinutes)
            .Select(p => p.ExeLabel)
            .ToList();

    public static IReadOnlyList<ProcessGroup> BuildGroupedRunningProcesses()
    {
        var running = ProcessWatcherService.GetRunningProcessNames();
        var launchers = LauncherCatalog.KnownLaunchers
            .Where(l => running.Contains(LauncherCatalog.Normalize(l)))
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var launcherNorm = new HashSet<string>(
            LauncherCatalog.KnownLaunchers.Select(LauncherCatalog.Normalize),
            StringComparer.OrdinalIgnoreCase);

        var gamesAndApps = running
            .Where(n => !launcherNorm.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n + ".exe")
            .ToList();

        var allRunning = running
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n + ".exe")
            .ToList();

        return
        [
            new ProcessGroup { Header = "Running games & apps", Items = gamesAndApps },
            new ProcessGroup { Header = "Launchers", Items = launchers },
            new ProcessGroup { Header = "All running", Items = allRunning },
        ];
    }

    public static IReadOnlyList<string> GetRunningExesExcludingLaunchers()
    {
        var running = ProcessWatcherService.GetRunningProcessNames();
        var launcherNorm = new HashSet<string>(
            LauncherCatalog.KnownLaunchers.Select(LauncherCatalog.Normalize),
            StringComparer.OrdinalIgnoreCase);

        return running
            .Where(n => !launcherNorm.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n + ".exe")
            .ToList();
    }
}
