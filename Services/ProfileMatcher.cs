using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Pure resolution logic for auto-swap profiles: given a profile and the set of
/// currently connected monitors, decide which monitor (if any) should become
/// primary. Kept free of Win32 calls so it can be unit-tested with simulated
/// monitor sets.
/// </summary>
public static class ProfileMatcher
{
    /// <summary>Result of evaluating whether a profile would match right now.</summary>
    public sealed class ProfileEvaluation
    {
        public bool ProfileEnabled { get; init; }
        public bool ProcessRunning { get; init; }
        public bool WouldMatch { get; init; }
        public MonitorInfo? TargetMonitor { get; init; }
        public bool TargetConnected { get; init; }
        public bool TargetIsPrimary { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    /// <summary>
    /// Simulates whether a profile would activate now: process running check plus
    /// target monitor resolution on the connected set.
    /// </summary>
    public static ProfileEvaluation Evaluate(
        AppProfile profile,
        ISet<string> runningProcessNames,
        IReadOnlyList<MonitorInfo> connectedMonitors,
        string? detectedLauncherChild = null)
    {
        if (profile is null)
        {
            return new ProfileEvaluation { Summary = "Profile is missing." };
        }

        if (!profile.Enabled)
        {
            return new ProfileEvaluation
            {
                ProfileEnabled = false,
                Summary = $"Profile '{profile.DisplayLabel}' is disabled.",
            };
        }

        var processRunning = IsProfileActive(profile, runningProcessNames, detectedLauncherChild);
        var target = ResolveTarget(profile, connectedMonitors);
        var connected = target is not null;
        var isPrimary = target?.IsPrimary == true;

        string summary;
        if (!processRunning)
        {
            var watch = DescribeWatchTargets(profile, detectedLauncherChild);
            summary = $"No match — neither '{watch}' is running.";
        }
        else if (!connected)
        {
            summary = $"Process is running, but target display '{profile.TargetMonitorName}' is not connected.";
        }
        else if (isPrimary)
        {
            summary = $"Match — '{profile.DisplayLabel}' is running and '{target!.Name}' is already primary.";
        }
        else
        {
            summary = $"Match — '{profile.DisplayLabel}' is running; would set primary to '{target!.Name}'.";
        }

        return new ProfileEvaluation
        {
            ProfileEnabled = true,
            ProcessRunning = processRunning,
            WouldMatch = processRunning && connected && !isPrimary,
            TargetMonitor = target,
            TargetConnected = connected,
            TargetIsPrimary = isPrimary,
            Summary = summary,
        };
    }

    /// <summary>
    /// Resolves the target monitor for a profile, or null when the configured
    /// display is not currently connected. Matches on friendly name first
    /// (stable across reconnects), then falls back to the GDI device name.
    /// </summary>
    public static MonitorInfo? ResolveTarget(AppProfile profile, IReadOnlyList<MonitorInfo> connectedMonitors)
    {
        if (profile is null || connectedMonitors is null || connectedMonitors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.TargetMonitorName))
        {
            var byName = connectedMonitors.FirstOrDefault(m =>
                string.Equals(m.Name, profile.TargetMonitorName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.TargetMonitorDeviceName))
        {
            var byDevice = connectedMonitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, profile.TargetMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (byDevice is not null)
            {
                return byDevice;
            }
        }

        return null;
    }

    /// <summary>True if the named process matches the profile (extension-insensitive).</summary>
    public static bool ProcessMatches(AppProfile profile, string runningProcessName)
    {
        if (profile is null || string.IsNullOrWhiteSpace(runningProcessName))
        {
            return false;
        }

        var running = NormalizeProcessName(runningProcessName);
        if (string.IsNullOrEmpty(running))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(profile.NormalizedProcessName) &&
            string.Equals(running, profile.NormalizedProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.HasResolvedTarget &&
               string.Equals(running, profile.NormalizedResolvedTarget, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when any configured trigger process for the profile is running.
    /// Launcher profiles match the resolved game exe and/or a child process
    /// detected after the launcher starts.
    /// </summary>
    public static bool IsProfileActive(
        AppProfile profile,
        ISet<string> runningProcessNames,
        string? detectedLauncherChild = null)
    {
        if (profile is null || runningProcessNames is null || runningProcessNames.Count == 0)
        {
            return false;
        }

        var isLauncher = LauncherCatalog.IsKnownLauncher(profile.ProcessName);

        if (!isLauncher &&
            !string.IsNullOrEmpty(profile.NormalizedProcessName) &&
            runningProcessNames.Contains(profile.NormalizedProcessName))
        {
            return true;
        }

        if (profile.HasResolvedTarget &&
            runningProcessNames.Contains(profile.NormalizedResolvedTarget))
        {
            return true;
        }

        if (isLauncher)
        {
            if (!profile.HasResolvedTarget &&
                !string.IsNullOrEmpty(profile.NormalizedProcessName) &&
                runningProcessNames.Contains(profile.NormalizedProcessName))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(detectedLauncherChild))
            {
                var child = NormalizeProcessName(detectedLauncherChild);
                if (profile.HasResolvedTarget)
                {
                    if (string.Equals(child, profile.NormalizedResolvedTarget, StringComparison.OrdinalIgnoreCase) &&
                        runningProcessNames.Contains(child))
                    {
                        return true;
                    }
                }
                else if (profile.MatchLauncherChildren && runningProcessNames.Contains(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string DescribeWatchTargets(AppProfile profile, string? detectedLauncherChild)
    {
        if (LauncherCatalog.IsKnownLauncher(profile.ProcessName))
        {
            if (profile.HasResolvedTarget)
            {
                return profile.NormalizedResolvedTarget;
            }

            if (!string.IsNullOrWhiteSpace(detectedLauncherChild))
            {
                return $"{profile.NormalizedProcessName} child ({detectedLauncherChild})";
            }

            return profile.MatchLauncherChildren
                ? $"{profile.NormalizedProcessName} or game child"
                : profile.NormalizedProcessName;
        }

        return profile.HasResolvedTarget
            ? $"{profile.NormalizedProcessName} or {profile.NormalizedResolvedTarget}"
            : profile.NormalizedProcessName;
    }

    private static string NormalizeProcessName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }
}
