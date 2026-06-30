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
            if (!primary.IsPrimary)
            {
                displayManager.SetPrimaryByDeviceName(primary.DeviceName);
            }

            foreach (var monitor in monitors)
            {
                if (!preset.MonitorModes.TryGetValue(monitor.DeviceName, out var modePreset))
                {
                    continue;
                }

                displayManager.ApplyDisplayMode(monitor.DeviceName, modePreset.ToDisplayMode());
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
