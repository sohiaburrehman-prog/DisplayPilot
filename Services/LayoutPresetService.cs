using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Captures and applies saved monitor layout presets.</summary>
public static class LayoutPresetService
{
    public sealed class ApplyResult
    {
        public bool Applied { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static LayoutPreset CaptureCurrent(string name, DisplayManager displayManager)
    {
        var monitors = displayManager.GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        var preset = new LayoutPreset
        {
            Name = name.Trim(),
            PrimaryMonitorDeviceName = primary?.DeviceName ?? string.Empty,
        };

        foreach (var monitor in monitors)
        {
            var mode = displayManager.GetCurrentMode(monitor.DeviceName);
            if (mode is not null)
            {
                preset.MonitorModes[monitor.DeviceName] = DisplayModePreset.FromDisplayMode(mode);
            }
        }

        return preset;
    }

    public static ApplyResult TryApply(LayoutPreset preset, AppSettings settings, DisplayManager displayManager)
    {
        if (preset is null || string.IsNullOrWhiteSpace(preset.PrimaryMonitorDeviceName))
        {
            return new ApplyResult { Message = "Preset has no primary monitor saved." };
        }

        var monitors = displayManager.GetMonitors();
        var primary = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, preset.PrimaryMonitorDeviceName, StringComparison.OrdinalIgnoreCase));

        if (primary is null)
        {
            return new ApplyResult
            {
                Message = $"Primary monitor {preset.PrimaryMonitorDeviceName} is not connected.",
            };
        }

        try
        {
            var requestedModes = monitors
                .Where(m => preset.MonitorModes.ContainsKey(m.DeviceName))
                .Select(m => (Monitor: m, Mode: preset.MonitorModes[m.DeviceName].ToDisplayMode()))
                .ToList();

            // Preflight the complete preset before making the first change.
            foreach (var requested in requestedModes)
            {
                displayManager.TestDisplayMode(requested.Monitor.DeviceName, requested.Mode);
            }

            var originalPrimaryDevice = monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName;
            var originalModes = requestedModes.ToDictionary(
                requested => requested.Monitor.DeviceName,
                requested => displayManager.GetCurrentMode(requested.Monitor.DeviceName),
                StringComparer.OrdinalIgnoreCase);
            var changedDevices = new List<string>();

            try
            {
                foreach (var requested in requestedModes)
                {
                    var current = originalModes[requested.Monitor.DeviceName];
                    if (current is not null && current.Equals(requested.Mode))
                    {
                        continue;
                    }

                    displayManager.ApplyDisplayMode(requested.Monitor.DeviceName, requested.Mode);
                    changedDevices.Add(requested.Monitor.DeviceName);
                }

                if (!primary.IsPrimary)
                {
                    displayManager.SetPrimaryByDeviceName(primary.DeviceName);
                }
            }
            catch (Exception applyError)
            {
                var rollbackErrors = new List<string>();
                foreach (var device in changedDevices.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (originalModes[device] is { } originalMode)
                        {
                            displayManager.ApplyDisplayMode(device, originalMode);
                        }
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors.Add($"{device}: {rollbackError.Message}");
                    }
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(originalPrimaryDevice))
                    {
                        displayManager.SetPrimaryByDeviceName(originalPrimaryDevice);
                    }
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add($"primary: {rollbackError.Message}");
                }

                var rollbackSummary = rollbackErrors.Count == 0
                    ? "Original display settings were restored."
                    : $"Rollback also had errors: {string.Join("; ", rollbackErrors)}";
                throw new InvalidOperationException($"{applyError.Message} {rollbackSummary}", applyError);
            }

            var label = MonitorDisplayHelper.GetDisplayName(primary, settings);
            AppLogger.Log($"Layout preset '{preset.Name}' applied — primary {label}.");
            return new ApplyResult
            {
                Applied = true,
                Message = $"Preset \"{preset.Name}\" applied — {label} is primary.",
            };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Layout preset apply failed [{preset.Name}]: {ex.Message}");
            return new ApplyResult { Message = ex.Message };
        }
    }
}
