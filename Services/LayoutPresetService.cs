using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Captures, previews, and transactionally applies saved display scenes.</summary>
public static class LayoutPresetService
{
    internal enum SceneApplyStage
    {
        BuildDesiredState,
        Preflight,
        DescribeChanges,
        CaptureRollback,
        ApplyTopology,
        ApplyHdr,
        VerifyAppliedState,
        RollbackTopology,
        RollbackHdr,
    }

    internal interface ISceneDisplayOperations
    {
        IReadOnlyList<MonitorInfo> GetMonitors();
        DisplaySceneMonitorState? GetCurrentSceneState(string deviceName);
        void TestDisplayScene(
            IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
            string primaryDeviceName);
        void ApplyDisplaySceneConfiguration(
            IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
            string primaryDeviceName);
        DisplayManager.HdrStatus? GetHdrStatus(string deviceName);
        void SetHdrEnabled(string deviceName, bool enable);
    }

    private sealed class DisplayManagerSceneOperations(DisplayManager displayManager) : ISceneDisplayOperations
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => displayManager.GetMonitors();

        public DisplaySceneMonitorState? GetCurrentSceneState(string deviceName) =>
            displayManager.GetCurrentSceneState(deviceName);

        public void TestDisplayScene(
            IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
            string primaryDeviceName) =>
            displayManager.TestDisplayScene(states, primaryDeviceName);

        public void ApplyDisplaySceneConfiguration(
            IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
            string primaryDeviceName) =>
            displayManager.ApplyDisplaySceneConfiguration(states, primaryDeviceName);

        public DisplayManager.HdrStatus? GetHdrStatus(string deviceName) =>
            displayManager.GetHdrStatus(deviceName);

        public void SetHdrEnabled(string deviceName, bool enable) =>
            displayManager.SetHdrEnabled(deviceName, enable);
    }

    public sealed class ApplyResult
    {
        public bool Applied { get; init; }
        public bool IsPreview { get; init; }
        public bool Valid { get; init; }
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<string> Changes { get; init; } = Array.Empty<string>();
        public LayoutPreset? RollbackScene { get; init; }
        internal SceneApplyStage? FailedStage { get; init; }
        internal bool RollbackAttempted { get; init; }
        internal bool RollbackSucceeded { get; init; }
    }

    public static LayoutPreset CaptureCurrent(string name, DisplayManager displayManager) =>
        CaptureCurrent(name, new DisplayManagerSceneOperations(displayManager));

    internal static LayoutPreset CaptureCurrent(string name, ISceneDisplayOperations displayManager)
    {
        var monitors = displayManager.GetMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        var scene = new LayoutPreset
        {
            Name = name.Trim(),
            PrimaryMonitorDeviceName = primary?.DeviceName ?? string.Empty,
        };

        foreach (var monitor in monitors)
        {
            var state = displayManager.GetCurrentSceneState(monitor.DeviceName);
            if (state is null)
            {
                continue;
            }

            scene.MonitorStates[monitor.DeviceName] = state;
            scene.MonitorModes[monitor.DeviceName] = state.ToLegacyMode();
        }

        return scene;
    }

    public static ApplyResult Preview(LayoutPreset scene, AppSettings settings, DisplayManager displayManager) =>
        Preview(scene, settings, new DisplayManagerSceneOperations(displayManager));

    internal static ApplyResult Preview(
        LayoutPreset scene,
        AppSettings settings,
        ISceneDisplayOperations displayManager)
    {
        try
        {
            var desired = BuildDesiredStates(scene, displayManager);
            displayManager.TestDisplayScene(desired, scene.PrimaryMonitorDeviceName);
            var changes = DescribeChanges(scene, desired, settings, displayManager);
            return new ApplyResult
            {
                IsPreview = true,
                Valid = true,
                Message = changes.Count == 0
                    ? $"Scene \"{scene.Name}\" already matches the active display configuration."
                    : $"Scene \"{scene.Name}\" is ready. {changes.Count} change(s) would be applied. Windows display scaling is not changed.",
                Changes = changes,
            };
        }
        catch (Exception ex)
        {
            return new ApplyResult { IsPreview = true, Message = ex.Message };
        }
    }

    public static ApplyResult TryApply(LayoutPreset scene, AppSettings settings, DisplayManager displayManager) =>
        TryApply(scene, settings, new DisplayManagerSceneOperations(displayManager));

    internal static ApplyResult TryApply(
        LayoutPreset scene,
        AppSettings settings,
        ISceneDisplayOperations displayManager,
        Action<SceneApplyStage>? beforeStage = null)
    {
        if (scene is null || string.IsNullOrWhiteSpace(scene.PrimaryMonitorDeviceName))
        {
            return new ApplyResult { Message = "Scene has no primary monitor saved." };
        }

        LayoutPreset? rollback = null;
        var stage = SceneApplyStage.BuildDesiredState;
        try
        {
            beforeStage?.Invoke(stage);
            var desired = BuildDesiredStates(scene, displayManager);

            stage = SceneApplyStage.Preflight;
            beforeStage?.Invoke(stage);
            displayManager.TestDisplayScene(desired, scene.PrimaryMonitorDeviceName);

            stage = SceneApplyStage.DescribeChanges;
            beforeStage?.Invoke(stage);
            var changes = DescribeChanges(scene, desired, settings, displayManager);

            stage = SceneApplyStage.CaptureRollback;
            beforeStage?.Invoke(stage);
            rollback = CaptureCurrent("Automatic rollback", displayManager);

            stage = SceneApplyStage.ApplyTopology;
            beforeStage?.Invoke(stage);
            displayManager.ApplyDisplaySceneConfiguration(desired, scene.PrimaryMonitorDeviceName);

            stage = SceneApplyStage.ApplyHdr;
            beforeStage?.Invoke(stage);
            ApplyHdrStates(desired, displayManager);

            stage = SceneApplyStage.VerifyAppliedState;
            beforeStage?.Invoke(stage);
            var monitors = displayManager.GetMonitors();
            var primary = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, scene.PrimaryMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (primary is null || !primary.IsPrimary)
            {
                throw new InvalidOperationException(
                    $"Scene verification failed: '{scene.PrimaryMonitorDeviceName}' is not primary.");
            }

            foreach (var (deviceName, expected) in desired)
            {
                var actual = displayManager.GetCurrentSceneState(deviceName);
                if (actual is null ||
                    actual.Width != expected.Width ||
                    actual.Height != expected.Height ||
                    actual.RefreshRateHz != expected.RefreshRateHz ||
                    actual.PositionX != expected.PositionX ||
                    actual.PositionY != expected.PositionY ||
                    actual.Orientation != expected.Orientation ||
                    (expected.HdrEnabled.HasValue && actual.HdrEnabled != expected.HdrEnabled))
                {
                    throw new InvalidOperationException(
                        $"Scene verification failed: '{deviceName}' does not match the requested state.");
                }
            }
            var label = MonitorDisplayHelper.GetDisplayName(primary, settings);
            AppLogger.Log($"Display scene '{scene.Name}' applied — primary {label}.");
            return new ApplyResult
            {
                Applied = true,
                Valid = true,
                Message = changes.Count == 0
                    ? $"Scene \"{scene.Name}\" already matched the current setup."
                    : $"Scene \"{scene.Name}\" applied — {label} is primary.",
                Changes = changes,
                RollbackScene = rollback,
            };
        }
        catch (Exception applyError)
        {
            var failedStage = stage;
            var rollbackErrors = new List<string>();
            var rollbackSucceeded = false;
            var rollbackAttempted = false;
            if (rollback?.MonitorStates.Count > 0 && !string.IsNullOrWhiteSpace(rollback.PrimaryMonitorDeviceName))
            {
                rollbackAttempted = true;
                try
                {
                    stage = SceneApplyStage.RollbackTopology;
                    beforeStage?.Invoke(stage);
                    displayManager.ApplyDisplaySceneConfiguration(
                        rollback.MonitorStates,
                        rollback.PrimaryMonitorDeviceName);

                    stage = SceneApplyStage.RollbackHdr;
                    beforeStage?.Invoke(stage);
                    ApplyHdrStates(rollback.MonitorStates, displayManager);
                    rollbackSucceeded = true;
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError.Message);
                }
            }

            // Only claim restore when a rollback snapshot actually ran successfully.
            var rollbackSummary = rollbackSucceeded
                ? "Original display settings were restored."
                : rollbackErrors.Count > 0
                    ? $"Rollback also failed: {string.Join("; ", rollbackErrors)}"
                    : "Original display settings were not restored.";
            var message = $"{applyError.Message} {rollbackSummary}";
            AppLogger.Log($"Display scene apply failed [{scene?.Name}]: {message}");
            return new ApplyResult
            {
                Message = message,
                FailedStage = failedStage,
                RollbackAttempted = rollbackAttempted,
                RollbackSucceeded = rollbackSucceeded,
            };
        }
    }

    /// <summary>Restores a snapshot returned by a successful scene apply.</summary>
    public static ApplyResult TryRestore(
        LayoutPreset rollbackScene,
        AppSettings settings,
        DisplayManager displayManager)
    {
        ArgumentNullException.ThrowIfNull(rollbackScene);
        var result = TryApply(rollbackScene, settings, displayManager);
        if (result.Applied)
        {
            AppLogger.Log("Display scene confirmation expired or was rejected; previous scene restored.");
        }
        return result;
    }

    /// <summary>Validates and normalizes a standalone imported scene.</summary>
    public static bool TryNormalizeImported(LayoutPreset? scene, out string? error)
    {
        error = null;
        if (scene is null)
        {
            error = "The file does not contain a display scene.";
            return false;
        }
        scene.Name = scene.Name?.Trim() ?? string.Empty;
        scene.PrimaryMonitorDeviceName ??= string.Empty;
        scene.MonitorModes ??= new Dictionary<string, DisplayModePreset>(StringComparer.OrdinalIgnoreCase);
        scene.MonitorStates ??= new Dictionary<string, DisplaySceneMonitorState>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(scene.Name))
        {
            error = "The scene has no name.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(scene.PrimaryMonitorDeviceName))
        {
            error = "The scene has no primary monitor.";
            return false;
        }
        if (scene.MonitorStates.Count == 0 && scene.MonitorModes.Count == 0)
        {
            error = "The scene has no monitor state.";
            return false;
        }
        foreach (var (device, state) in scene.MonitorStates)
        {
            if (string.IsNullOrWhiteSpace(device) || state is null ||
                state.Width <= 0 || state.Height <= 0 || state.Orientation > 3)
            {
                error = $"The scene contains invalid monitor state for '{device}'.";
                return false;
            }
        }
        if (string.IsNullOrWhiteSpace(scene.Id))
        {
            scene.Id = Guid.NewGuid().ToString("N");
        }
        return true;
    }

    private static Dictionary<string, DisplaySceneMonitorState> BuildDesiredStates(
        LayoutPreset scene,
        ISceneDisplayOperations displayManager)
    {
        if (scene.MonitorStates.Count > 0)
        {
            return scene.MonitorStates.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
        }

        // Schema v5 presets become safe scenes by retaining the live topology,
        // orientation, and HDR state while applying their saved modes.
        var desired = new Dictionary<string, DisplaySceneMonitorState>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in displayManager.GetMonitors())
        {
            var state = displayManager.GetCurrentSceneState(monitor.DeviceName);
            if (state is null)
            {
                continue;
            }

            if (scene.MonitorModes.TryGetValue(monitor.DeviceName, out var legacyMode))
            {
                state.Width = legacyMode.Width;
                state.Height = legacyMode.Height;
                state.RefreshRateHz = legacyMode.RefreshRateHz;
            }

            desired[monitor.DeviceName] = state;
        }

        if (desired.Count == 0)
        {
            throw new InvalidOperationException("Scene has no monitor state saved.");
        }

        return desired;
    }

    private static IReadOnlyList<string> DescribeChanges(
        LayoutPreset scene,
        IReadOnlyDictionary<string, DisplaySceneMonitorState> desired,
        AppSettings settings,
        ISceneDisplayOperations displayManager)
    {
        var monitors = displayManager.GetMonitors();
        var changes = new List<string>();
        foreach (var monitor in monitors)
        {
            if (!desired.TryGetValue(monitor.DeviceName, out var target))
            {
                continue;
            }

            var current = displayManager.GetCurrentSceneState(monitor.DeviceName);
            var label = MonitorDisplayHelper.GetDisplayName(monitor, settings);
            if (current is null)
            {
                changes.Add($"{label}: state could not be read");
                continue;
            }

            var parts = new List<string>();
            if (current.Width != target.Width || current.Height != target.Height || current.RefreshRateHz != target.RefreshRateHz)
            {
                parts.Add($"{target.Width}×{target.Height} · {target.RefreshRateHz} Hz");
            }
            if (current.PositionX != target.PositionX || current.PositionY != target.PositionY)
            {
                parts.Add($"position {target.PositionX},{target.PositionY}");
            }
            if (current.Orientation != target.Orientation)
            {
                parts.Add($"rotation {OrientationLabel(target.Orientation)}");
            }
            if (target.HdrEnabled.HasValue && current.HdrEnabled != target.HdrEnabled)
            {
                parts.Add($"HDR {(target.HdrEnabled.Value ? "on" : "off")}");
            }
            if (parts.Count > 0)
            {
                changes.Add($"{label}: {string.Join(", ", parts)}");
            }
        }

        var currentPrimary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (!string.Equals(currentPrimary?.DeviceName, scene.PrimaryMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            var target = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, scene.PrimaryMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            changes.Add($"Primary: {(target is null ? scene.PrimaryMonitorDeviceName : MonitorDisplayHelper.GetDisplayName(target, settings))}");
        }

        return changes;
    }

    private static void ApplyHdrStates(
        IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
        ISceneDisplayOperations displayManager)
    {
        foreach (var (deviceName, state) in states)
        {
            if (!state.HdrEnabled.HasValue)
            {
                continue;
            }

            var current = displayManager.GetHdrStatus(deviceName);
            if (current?.Supported == true && current.Enabled != state.HdrEnabled.Value)
            {
                displayManager.SetHdrEnabled(deviceName, state.HdrEnabled.Value);
            }
        }
    }

    private static string OrientationLabel(uint orientation) => orientation switch
    {
        1 => "90°",
        2 => "180°",
        3 => "270°",
        _ => "0°",
    };
}
