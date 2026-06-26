using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Tracks processes that appear after a game-store launcher starts, so
/// auto-swap profiles can match the actual game exe instead of the launcher.
/// </summary>
public static class LauncherChildTracker
{
    public sealed class WatchState
    {
        public HashSet<string> BaselineProcessNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime LauncherSeenUtc { get; set; }
        public string? DetectedChild { get; set; }
        public bool WaitingLogged { get; set; }

        public void Reset()
        {
            BaselineProcessNames.Clear();
            LauncherSeenUtc = DateTime.MinValue;
            DetectedChild = null;
            WaitingLogged = false;
        }
    }

    public static bool IsLauncherProfile(AppProfile profile) =>
        profile is not null && LauncherCatalog.IsKnownLauncher(profile.ProcessName);

    /// <summary>
    /// Updates watch state for a launcher profile and returns the detected child
    /// process name (without .exe) when one is identified.
    /// </summary>
    public static string? UpdateWatchState(
        AppProfile profile,
        WatchState state,
        ISet<string> currentRunning,
        bool launcherWasRunning,
        bool launcherIsRunning)
    {
        if (!launcherIsRunning)
        {
            state.Reset();
            return null;
        }

        if (!launcherWasRunning)
        {
            state.BaselineProcessNames = new HashSet<string>(currentRunning, StringComparer.OrdinalIgnoreCase);
            state.LauncherSeenUtc = DateTime.UtcNow;
            state.DetectedChild = null;
            state.WaitingLogged = false;

            var waitTarget = profile.HasResolvedTarget
                ? profile.NormalizedResolvedTarget
                : "game process";
            AppLogger.Log(
                $"Launcher {profile.NormalizedProcessName} detected, waiting for {waitTarget} " +
                $"(profile [{profile.DisplayLabel}]).");
        }

        if (profile.HasResolvedTarget &&
            currentRunning.Contains(profile.NormalizedResolvedTarget))
        {
            if (!string.Equals(state.DetectedChild, profile.NormalizedResolvedTarget, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Log(
                    $"Child process matched: {profile.NormalizedResolvedTarget} " +
                    $"(profile [{profile.DisplayLabel}]).");
            }

            state.DetectedChild = profile.NormalizedResolvedTarget;
            return profile.NormalizedResolvedTarget;
        }

        var newProcesses = currentRunning
            .Where(n => !state.BaselineProcessNames.Contains(n))
            .Where(n => !ProcessPickerHelper.IsExcludedProcess(n))
            .ToList();

        if (profile.HasResolvedTarget)
        {
            var match = newProcesses.FirstOrDefault(n =>
                string.Equals(n, profile.NormalizedResolvedTarget, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                AppLogger.Log($"Child process matched: {match} (profile [{profile.DisplayLabel}]).");
                state.DetectedChild = match;
                return match;
            }

            return state.DetectedChild;
        }

        if (!profile.MatchLauncherChildren)
        {
            return null;
        }

        if (state.DetectedChild is not null &&
            currentRunning.Contains(state.DetectedChild))
        {
            return state.DetectedChild;
        }

        var child = newProcesses.FirstOrDefault();
        if (child is not null)
        {
            AppLogger.Log($"Child process matched: {child} (profile [{profile.DisplayLabel}]).");
            state.DetectedChild = child;
            return child;
        }

        return null;
    }
}
