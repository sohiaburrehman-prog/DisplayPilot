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

        var running = runningProcessName.Trim();
        if (running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            running = running[..^4];
        }

        return !string.IsNullOrEmpty(profile.NormalizedProcessName) &&
               string.Equals(running, profile.NormalizedProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
