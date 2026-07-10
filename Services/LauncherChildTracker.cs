using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Tracks launcher descendants by PID ancestry. This deliberately avoids the
/// old "first new process on the machine" heuristic, which could select an
/// unrelated updater or app started at the same time as a game.
/// </summary>
public static class LauncherChildTracker
{
    public sealed record RunningProcess(uint ProcessId, string Name);

    public sealed record ProcessStart(
        uint ProcessId,
        uint ParentProcessId,
        string Name,
        DateTime SeenUtc);

    public sealed class WatchState
    {
        public HashSet<uint> KnownLauncherProcessIds { get; } = new();
        public string? DetectedChild { get; set; }

        public void Reset()
        {
            KnownLauncherProcessIds.Clear();
            DetectedChild = null;
        }
    }

    public static bool IsLauncherProfile(AppProfile profile) =>
        profile is not null && LauncherCatalog.IsKnownLauncher(profile.ProcessName);

    /// <summary>
    /// Returns the configured target when it is running, otherwise identifies
    /// a currently-running descendant of the configured launcher from WMI
    /// process-start ancestry. A detected child remains active after a launcher
    /// exits and is cleared only when that child exits.
    /// </summary>
    public static string? UpdateWatchState(
        AppProfile profile,
        WatchState state,
        IReadOnlyList<RunningProcess> currentProcesses,
        IReadOnlyList<ProcessStart> recentStarts)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(state);

        var runningNames = currentProcesses
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (profile.HasResolvedTarget && runningNames.Contains(profile.NormalizedResolvedTarget))
        {
            state.DetectedChild = profile.NormalizedResolvedTarget;
            return state.DetectedChild;
        }

        var previouslyDetectedChild = state.DetectedChild;
        if (!string.IsNullOrWhiteSpace(previouslyDetectedChild) && runningNames.Contains(previouslyDetectedChild))
        {
            return previouslyDetectedChild;
        }

        state.DetectedChild = null;

        var launcherPids = currentProcesses
            .Where(p => string.Equals(p.Name, profile.NormalizedProcessName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ProcessId)
            .ToHashSet();
        state.KnownLauncherProcessIds.UnionWith(launcherPids);

        // A previously tracked game has exited and its launcher is already
        // gone. Clear stale launcher PIDs before Windows can reuse them.
        if (!string.IsNullOrWhiteSpace(previouslyDetectedChild) && launcherPids.Count == 0)
        {
            state.Reset();
            return null;
        }

        if (!profile.MatchLauncherChildren || state.KnownLauncherProcessIds.Count == 0)
        {
            if (launcherPids.Count == 0)
            {
                state.Reset();
            }

            return null;
        }

        var runningPids = currentProcesses.Select(p => p.ProcessId).ToHashSet();
        var startsByPid = recentStarts.ToDictionary(p => p.ProcessId, p => p);

        var child = recentStarts
            .Where(p => runningPids.Contains(p.ProcessId))
            .Where(p => !ProcessPickerHelper.IsExcludedProcess(p.Name))
            .Where(p => IsDescendantOfLauncher(p, state.KnownLauncherProcessIds, startsByPid))
            .OrderBy(p => p.SeenUtc)
            .FirstOrDefault();

        if (child is not null)
        {
            state.DetectedChild = child.Name;
            AppLogger.Log(
                $"Launcher descendant matched by PID ancestry: {child.Name} " +
                $"(profile [{profile.DisplayLabel}], pid {child.ProcessId}, parent {child.ParentProcessId}).");
            return child.Name;
        }

        if (launcherPids.Count == 0)
        {
            state.Reset();
        }

        return null;
    }

    private static bool IsDescendantOfLauncher(
        ProcessStart process,
        ISet<uint> launcherPids,
        IReadOnlyDictionary<uint, ProcessStart> startsByPid)
    {
        var parentPid = process.ParentProcessId;
        var visited = new HashSet<uint>();

        while (parentPid != 0 && visited.Add(parentPid))
        {
            if (launcherPids.Contains(parentPid))
            {
                return true;
            }

            if (!startsByPid.TryGetValue(parentPid, out var parent))
            {
                return false;
            }

            parentPid = parent.ParentProcessId;
        }

        return false;
    }
}
