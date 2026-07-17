using System.Text;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Builds a read-only explanation of the live display state and saved scenes.</summary>
public static class DisplaySceneDiagnosticsService
{
    public sealed class MonitorEntry
    {
        public string Label { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }
        public DisplaySceneMonitorState? State { get; init; }
    }

    public sealed class SceneEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public string PrimaryLabel { get; init; } = string.Empty;
        public bool IsFullScene { get; init; }
        public int MonitorCount { get; init; }
        public bool CanApply { get; init; }
        public string Summary { get; init; } = string.Empty;
        public IReadOnlyList<string> Changes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ReferencedByProfiles { get; init; } = Array.Empty<string>();
    }

    public sealed class Snapshot
    {
        public IReadOnlyList<MonitorEntry> Monitors { get; init; } = Array.Empty<MonitorEntry>();
        public IReadOnlyList<SceneEntry> Scenes { get; init; } = Array.Empty<SceneEntry>();
        public DateTime CapturedUtc { get; init; }
    }

    public static Snapshot Capture(AppSettings settings, DisplayManager displayManager)
    {
        var monitors = displayManager.GetMonitors();
        var monitorEntries = monitors.Select(monitor => new MonitorEntry
        {
            Label = MonitorDisplayHelper.GetDisplayName(monitor, settings),
            DeviceName = monitor.DeviceName,
            IsPrimary = monitor.IsPrimary,
            State = displayManager.GetCurrentSceneState(monitor.DeviceName),
        }).ToList();

        var sceneEntries = settings.LayoutPresets.Select(scene =>
        {
            var primary = monitors.FirstOrDefault(monitor => string.Equals(
                monitor.DeviceName,
                scene.PrimaryMonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));
            var preview = LayoutPresetService.Preview(scene, settings, displayManager);
            var references = settings.Profiles
                .Where(profile => string.Equals(profile.DisplaySceneId, scene.Id, StringComparison.Ordinal))
                .Select(profile => profile.DisplayLabel)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new SceneEntry
            {
                Name = scene.Name,
                Id = scene.Id,
                PrimaryLabel = primary is null
                    ? $"{scene.PrimaryMonitorDeviceName} (not connected)"
                    : MonitorDisplayHelper.GetDisplayName(primary, settings),
                IsFullScene = scene.IsFullScene,
                MonitorCount = scene.IsFullScene ? scene.MonitorStates.Count : scene.MonitorModes.Count,
                CanApply = preview.Valid,
                Summary = preview.Message,
                Changes = preview.Changes,
                ReferencedByProfiles = references,
            };
        }).ToList();

        return new Snapshot
        {
            Monitors = monitorEntries,
            Scenes = sceneEntries,
            CapturedUtc = DateTime.UtcNow,
        };
    }

    public static string FormatReport(Snapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CURRENT DISPLAY STATE");
        if (snapshot.Monitors.Count == 0)
        {
            builder.AppendLine("No active displays were detected.");
        }
        else
        {
            foreach (var monitor in snapshot.Monitors)
            {
                builder.AppendLine($"- {monitor.Label}{(monitor.IsPrimary ? " [PRIMARY]" : string.Empty)}");
                builder.AppendLine($"  Device: {monitor.DeviceName}");
                builder.AppendLine(monitor.State is null
                    ? "  State: could not be read"
                    : $"  State: {DescribeState(monitor.State)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("SAVED DISPLAY SCENES");
        if (snapshot.Scenes.Count == 0)
        {
            builder.AppendLine("No scenes are saved. Capture the current scene from the Display scenes tab.");
            return builder.ToString().TrimEnd();
        }

        foreach (var scene in snapshot.Scenes)
        {
            builder.AppendLine($"[{(scene.CanApply ? "READY" : "BLOCKED")}] {scene.Name}");
            builder.AppendLine($"  ID: {scene.Id}");
            builder.AppendLine($"  Primary: {scene.PrimaryLabel}");
            builder.AppendLine($"  Scope: {(scene.IsFullScene ? "complete" : "legacy mode-only")} state for {scene.MonitorCount} display(s)");
            builder.AppendLine($"  {scene.Summary}");
            foreach (var change in scene.Changes)
            {
                builder.AppendLine($"  Would change: {change}");
            }
            if (scene.ReferencedByProfiles.Count > 0)
            {
                builder.AppendLine($"  Used by profiles: {string.Join(", ", scene.ReferencedByProfiles)}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeState(DisplaySceneMonitorState state)
    {
        var hdr = state.HdrEnabled.HasValue ? (state.HdrEnabled.Value ? "on" : "off") : "unavailable";
        return $"{state.Width}×{state.Height} · {state.RefreshRateHz} Hz · " +
               $"position {state.PositionX},{state.PositionY} · rotation {OrientationLabel(state.Orientation)} · HDR {hdr}";
    }

    private static string OrientationLabel(uint orientation) => orientation switch
    {
        1 => "90°",
        2 => "180°",
        3 => "270°",
        _ => "0°",
    };
}
