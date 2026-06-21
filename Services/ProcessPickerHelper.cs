using System.Diagnostics;

namespace PrimaryDisplaySwap.Services;

/// <summary>Builds grouped process lists for profile and wizard pickers.</summary>
public static class ProcessPickerHelper
{
    public sealed class ProcessGroup
    {
        public required string Header { get; init; }
        public required IReadOnlyList<string> Items { get; init; }
    }

    public static IReadOnlyList<ProcessGroup> BuildGroupedRunningProcesses()
    {
        var running = GetRunningProcessNames();
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
        var running = GetRunningProcessNames();
        var launcherNorm = new HashSet<string>(
            LauncherCatalog.KnownLaunchers.Select(LauncherCatalog.Normalize),
            StringComparer.OrdinalIgnoreCase);

        return running
            .Where(n => !launcherNorm.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n + ".exe")
            .ToList();
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch
            {
                // Process exited between enumeration and read.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }
}
