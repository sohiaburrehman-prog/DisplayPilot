using System.Runtime.InteropServices;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Native;

using static PrimaryDisplaySwap.Native.DisplayInterop;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Enumerates monitors via GDI and changes the primary display. Tries the modern
/// DisplayConfig API first, then falls back to staged ChangeDisplaySettingsEx
/// with CDS_SET_PRIMARY when drivers reject SetDisplayConfig.
///
/// All methods are synchronous and can block for a few hundred milliseconds
/// while drivers apply a new configuration — call from a background thread
/// (Task.Run), never from the UI thread.
/// </summary>
public sealed class DisplayManager
{
    private readonly Func<IReadOnlyList<MonitorInfo>>? _monitorsProvider;

    public DisplayManager()
    {
    }

    /// <summary>Test seam: inject monitor snapshots so resolve/apply never crosses a re-enumeration.</summary>
    internal DisplayManager(Func<IReadOnlyList<MonitorInfo>> monitorsProvider)
    {
        _monitorsProvider = monitorsProvider ?? throw new ArgumentNullException(nameof(monitorsProvider));
    }

    /// <summary>When true, set-primary resolves the target but does not call Win32 display APIs.</summary>
    internal bool DryRun { get; set; }

    /// <summary>Last monitor selected by a dry-run set-primary / cycle / swap.</summary>
    internal MonitorInfo? LastDryRunPrimaryTarget { get; private set; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        if (_monitorsProvider is not null)
        {
            return _monitorsProvider();
        }

        var (friendlyNames, configBounds) = GetDisplayConfigDetails();
        var monitors = new List<MonitorInfo>();

        for (uint deviceIndex = 0; ; deviceIndex++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & DisplayDeviceAttachedToDesktop) == 0 ||
                (device.StateFlags & DisplayDeviceMirroringDriver) != 0)
            {
                continue;
            }

            var devMode = CreateDevMode();
            if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref devMode))
            {
                AppLogger.Log($"EnumDisplaySettings failed for {device.DeviceName}; skipping.");
                continue;
            }

            var name = friendlyNames.TryGetValue(device.DeviceName, out var friendly) && !string.IsNullOrWhiteSpace(friendly)
                ? friendly
                : $"Display {monitors.Count + 1}";

            var bounds = configBounds.TryGetValue(device.DeviceName, out var fromConfig)
                ? fromConfig
                : new DisplayBounds(
                    devMode.dmPositionX,
                    devMode.dmPositionY,
                    (int)devMode.dmPelsWidth,
                    (int)devMode.dmPelsHeight);

            monitors.Add(new MonitorInfo
            {
                Index = monitors.Count,
                DeviceName = device.DeviceName,
                Name = name,
                Width = bounds.Width,
                Height = bounds.Height,
                PositionX = bounds.X,
                PositionY = bounds.Y,
                IsPrimary = (device.StateFlags & DisplayDevicePrimaryDevice) != 0,
                RefreshRateHz = (int)devMode.dmDisplayFrequency
            });
        }

        return monitors;
    }

    /// <summary>Makes the monitor at <paramref name="monitorIndex"/> primary. Returns the new primary.</summary>
    public MonitorInfo SetPrimaryMonitor(int monitorIndex)
    {
        var monitors = GetMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        }

        if (monitors.Count <= 1)
        {
            throw new InvalidOperationException("Only one monitor is connected — nothing to swap.");
        }

        return SetPrimaryFromSnapshot(monitors, monitors[monitorIndex].DeviceName);
    }

    /// <summary>With exactly two monitors, makes the non-primary one primary. Returns the new primary.</summary>
    public MonitorInfo SwapPrimaryBetweenTwoMonitors()
    {
        var monitors = GetMonitors();
        if (monitors.Count != 2)
        {
            throw new InvalidOperationException("Quick swap is only available with exactly two monitors.");
        }

        var nonPrimary = monitors.First(m => !m.IsPrimary);
        return SetPrimaryFromSnapshot(monitors, nonPrimary.DeviceName);
    }

    /// <summary>
    /// Makes the monitor with the given GDI device name primary. Used by
    /// auto-swap profiles. Throws if the device is not currently connected.
    /// Resolves and applies against a single GetMonitors snapshot (no stale index).
    /// </summary>
    public MonitorInfo SetPrimaryByDeviceName(string deviceName)
    {
        var monitors = GetMonitors();
        return SetPrimaryFromSnapshot(monitors, deviceName);
    }

    /// <summary>
    /// Cycles the primary display to the next monitor in index order. With a
    /// single monitor this is a no-op (returns it unchanged).
    /// </summary>
    public MonitorInfo CyclePrimary()
    {
        var monitors = GetMonitors();
        if (monitors.Count <= 1)
        {
            throw new InvalidOperationException("Only one monitor is connected — nothing to cycle.");
        }

        var currentPrimaryIndex = monitors
            .Select((m, i) => (m, i))
            .FirstOrDefault(t => t.m.IsPrimary).i;

        var next = (currentPrimaryIndex + 1) % monitors.Count;
        return SetPrimaryFromSnapshot(monitors, monitors[next].DeviceName);
    }

    /// <summary>
    /// Resolves <paramref name="deviceName"/> inside <paramref name="monitors"/> and
    /// applies primary change using that same snapshot. Never re-enumerates by index.
    /// </summary>
    internal MonitorInfo SetPrimaryFromSnapshot(IReadOnlyList<MonitorInfo> monitors, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count <= 1)
        {
            throw new InvalidOperationException("Only one monitor is connected — nothing to swap.");
        }

        var target = ResolvePrimaryByDeviceName(monitors, deviceName);
        if (target.IsPrimary)
        {
            if (DryRun)
            {
                LastDryRunPrimaryTarget = target;
            }

            return target;
        }

        if (DryRun)
        {
            LastDryRunPrimaryTarget = target;
            return target;
        }

        string? lastError = null;

        if (TrySetPrimaryViaDisplayConfig(target, monitors, out var displayConfigError))
        {
            if (VerifyPrimary(target.DeviceName))
            {
                AppLogger.Log($"Primary display set via DisplayConfig to {target.Name} ({target.DeviceName}).");
                return GetMonitors().First(m => string.Equals(m.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase));
            }

            lastError = "DisplayConfig applied but the primary monitor did not change.";
            AppLogger.Log(lastError);
        }
        else
        {
            lastError = displayConfigError;
            AppLogger.Log($"DisplayConfig failed: {displayConfigError}");
        }

        if (TrySetPrimaryViaChangeDisplaySettings(target, monitors, out var cdsError))
        {
            if (VerifyPrimary(target.DeviceName))
            {
                AppLogger.Log($"Primary display set via ChangeDisplaySettingsEx to {target.Name} ({target.DeviceName}).");
                return GetMonitors().First(m => string.Equals(m.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase));
            }

            lastError = "ChangeDisplaySettingsEx applied but the primary monitor did not change.";
            AppLogger.Log(lastError);
        }
        else
        {
            lastError = cdsError;
            AppLogger.Log($"ChangeDisplaySettingsEx failed: {cdsError}");
        }

        throw new InvalidOperationException(lastError ?? "Could not set primary display.");
    }

    /// <summary>Finds a connected monitor by GDI device name within one snapshot.</summary>
    internal static MonitorInfo ResolvePrimaryByDeviceName(IReadOnlyList<MonitorInfo> monitors, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var target = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            throw new InvalidOperationException($"Display {deviceName} is not currently connected.");
        }

        return target;
    }

    /// <summary>
    /// Demonstrates the pre-fix race: resolve index in snapshot A, then index into
    /// snapshot B. Used by MonitorLogicTest to prove the stale-index failure mode.
    /// </summary>
    internal static MonitorInfo ResolvePrimaryByStaleIndex(
        IReadOnlyList<MonitorInfo> resolveSnapshot,
        IReadOnlyList<MonitorInfo> applySnapshot,
        string deviceName)
    {
        var index = ResolvePrimaryByDeviceName(resolveSnapshot, deviceName).Index;
        if (index < 0 || index >= applySnapshot.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return applySnapshot[index];
    }

    /// <summary>
    /// Switches the active display topology — the same four options as the
    /// Windows "Project" (Win+P) flyout. Uses SetDisplayConfig with an
    /// SDC_TOPOLOGY_* flag, which Windows persists for the current display set.
    /// </summary>
    public void SetProjectionMode(ProjectionMode mode)
    {
        var topology = mode switch
        {
            ProjectionMode.PcScreenOnly => SdcTopologyInternal,
            ProjectionMode.Duplicate => SdcTopologyClone,
            ProjectionMode.Extend => SdcTopologyExtend,
            ProjectionMode.SecondScreenOnly => SdcTopologyExternal,
            _ => SdcTopologyExtend,
        };

        var result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, topology | SdcApply);
        if (result != ErrorSuccess)
        {
            throw new InvalidOperationException(
                $"Could not switch to \"{mode.DisplayLabel()}\" (Win32 error {result}). " +
                "The connected displays may not support that projection mode.");
        }

        AppLogger.Log($"Projection mode set to {mode.DisplayLabel()}.");
    }

    /// <summary>
    /// Enumerates the display modes (resolution + refresh) the monitor reports,
    /// de-duplicated and sorted from highest to lowest. Returns an empty list on
    /// failure.
    /// </summary>
    public IReadOnlyList<DisplayMode> GetAvailableModes(string deviceName)
    {
        var modes = new List<DisplayMode>();
        var seen = new HashSet<DisplayMode>();

        try
        {
            for (var modeIndex = 0; ; modeIndex++)
            {
                var devMode = CreateDevMode();
                if (!EnumDisplaySettings(deviceName, modeIndex, ref devMode))
                {
                    break;
                }

                // Skip low colour-depth legacy modes.
                if (devMode.dmBitsPerPel < 16)
                {
                    continue;
                }

                var mode = new DisplayMode
                {
                    Width = (int)devMode.dmPelsWidth,
                    Height = (int)devMode.dmPelsHeight,
                    RefreshRateHz = (int)devMode.dmDisplayFrequency,
                    BitsPerPixel = (int)devMode.dmBitsPerPel,
                };

                if (mode.Width <= 0 || mode.Height <= 0)
                {
                    continue;
                }

                if (seen.Add(mode))
                {
                    modes.Add(mode);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"GetAvailableModes failed for {deviceName}: {ex.Message}");
        }

        return modes
            .OrderByDescending(m => (long)m.Width * m.Height)
            .ThenByDescending(m => m.RefreshRateHz)
            .ToList();
    }

    /// <summary>The monitor's current mode, or null if it cannot be read.</summary>
    public DisplayMode? GetCurrentMode(string deviceName)
    {
        var devMode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            return null;
        }

        return new DisplayMode
        {
            Width = (int)devMode.dmPelsWidth,
            Height = (int)devMode.dmPelsHeight,
            RefreshRateHz = (int)devMode.dmDisplayFrequency,
            BitsPerPixel = (int)devMode.dmBitsPerPel,
        };
    }

    /// <summary>
    /// Validates a resolution + refresh-rate change without applying it.
    /// </summary>
    public void TestDisplayMode(string deviceName, DisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        var devMode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            throw new InvalidOperationException($"Could not read current settings for {deviceName}.");
        }

        devMode.dmPelsWidth = (uint)mode.Width;
        devMode.dmPelsHeight = (uint)mode.Height;
        devMode.dmFields = DmPelsWidth | DmPelsHeight;
        if (mode.RefreshRateHz > 0)
        {
            devMode.dmDisplayFrequency = (uint)mode.RefreshRateHz;
            devMode.dmFields |= DmDisplayFrequency;
        }

        var test = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != DispChangeSuccessful)
        {
            throw new InvalidOperationException(
                $"{mode.Label} is not supported on this display — {DescribeDispChange(test)}.");
        }
    }

    /// <summary>Reads the scene properties that Windows exposes through DEVMODE plus HDR.</summary>
    public DisplaySceneMonitorState? GetCurrentSceneState(string deviceName)
    {
        var devMode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            return null;
        }

        var hdr = GetHdrStatus(deviceName);
        return new DisplaySceneMonitorState
        {
            Width = (int)devMode.dmPelsWidth,
            Height = (int)devMode.dmPelsHeight,
            RefreshRateHz = (int)devMode.dmDisplayFrequency,
            PositionX = devMode.dmPositionX,
            PositionY = devMode.dmPositionY,
            Orientation = devMode.dmDisplayOrientation,
            HdrEnabled = hdr?.Supported == true ? hdr.Enabled : null,
        };
    }

    /// <summary>
    /// Preflights every monitor in a scene without committing a change.
    /// Missing monitors and invalid orientation values fail the complete scene.
    /// </summary>
    public void TestDisplayScene(
        IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
        string primaryDeviceName)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count == 0)
        {
            throw new InvalidOperationException("Scene has no monitor state saved.");
        }

        var connected = GetMonitors()
            .Select(m => m.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!connected.Contains(primaryDeviceName))
        {
            throw new InvalidOperationException($"Scene primary display '{primaryDeviceName}' is not connected.");
        }

        foreach (var (deviceName, state) in states)
        {
            if (!connected.Contains(deviceName))
            {
                throw new InvalidOperationException($"Scene display '{deviceName}' is not connected.");
            }

            if (state.Orientation > 3)
            {
                throw new InvalidOperationException($"Scene has an invalid orientation for {deviceName}.");
            }

            var devMode = BuildSceneDevMode(deviceName, state);
            var test = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero);
            if (test != DispChangeSuccessful)
            {
                throw new InvalidOperationException(
                    $"Scene settings are not supported on {deviceName} — {DescribeDispChange(test)}.");
            }
        }
    }

    /// <summary>
    /// Applies all resolution, refresh, position, orientation, and primary
    /// changes as one staged desktop transaction. Call TestDisplayScene first.
    /// </summary>
    public void ApplyDisplaySceneConfiguration(
        IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
        string primaryDeviceName)
    {
        TestDisplayScene(states, primaryDeviceName);

        foreach (var (deviceName, state) in states)
        {
            var devMode = BuildSceneDevMode(deviceName, state);
            var flags = CdsUpdateRegistry | CdsNoReset;
            if (string.Equals(deviceName, primaryDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                flags |= CdsSetPrimary;
            }

            var staged = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
            if (staged != DispChangeSuccessful)
            {
                throw new InvalidOperationException(
                    $"Could not stage scene settings for {deviceName} — {DescribeDispChange(staged)}.");
            }
        }

        var applied = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (applied != DispChangeSuccessful)
        {
            throw new InvalidOperationException($"Could not commit the display scene — {DescribeDispChange(applied)}.");
        }

        AppLogger.Log($"Display scene configuration committed ({states.Count} monitor(s), primary {primaryDeviceName}).");
    }

    private static DEVMODE BuildSceneDevMode(string deviceName, DisplaySceneMonitorState state)
    {
        var devMode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            throw new InvalidOperationException($"Could not read current settings for {deviceName}.");
        }

        devMode.dmPelsWidth = (uint)state.Width;
        devMode.dmPelsHeight = (uint)state.Height;
        devMode.dmPositionX = state.PositionX;
        devMode.dmPositionY = state.PositionY;
        devMode.dmDisplayOrientation = state.Orientation;
        devMode.dmFields = DmPelsWidth | DmPelsHeight | DmPosition | DmDisplayOrientation;
        if (state.RefreshRateHz > 0)
        {
            devMode.dmDisplayFrequency = (uint)state.RefreshRateHz;
            devMode.dmFields |= DmDisplayFrequency;
        }

        return devMode;
    }

    /// <summary>
    /// Applies a resolution + refresh-rate change to one monitor. Validates with
    /// CDS_TEST first, then commits with CDS_UPDATEREGISTRY. Throws with a
    /// descriptive message on failure (leaving the current mode untouched).
    /// </summary>
    public void ApplyDisplayMode(string deviceName, DisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        TestDisplayMode(deviceName, mode);

        var devMode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            throw new InvalidOperationException($"Could not read current settings for {deviceName}.");
        }

        devMode.dmPelsWidth = (uint)mode.Width;
        devMode.dmPelsHeight = (uint)mode.Height;
        devMode.dmFields = DmPelsWidth | DmPelsHeight;

        if (mode.RefreshRateHz > 0)
        {
            devMode.dmDisplayFrequency = (uint)mode.RefreshRateHz;
            devMode.dmFields |= DmDisplayFrequency;
        }

        var apply = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (apply != DispChangeSuccessful && apply != DispChangeRestart)
        {
            throw new InvalidOperationException(
                $"Could not apply {mode.Label} — {DescribeDispChange(apply)}.");
        }

        AppLogger.Log($"Applied display mode {mode.Label} to {deviceName} (result {apply}).");
    }

    private static bool TrySetPrimaryViaDisplayConfig(MonitorInfo target, IReadOnlyList<MonitorInfo> monitors, out string error)
    {
        error = string.Empty;

        try
        {
            var (paths, modes) = QueryActiveConfig();
            var offsetX = target.PositionX;
            var offsetY = target.PositionY;

            for (var i = 0; i < modes.Length; i++)
            {
                if (modes[i].infoType != DISPLAYCONFIG_MODE_INFO.TypeSource)
                {
                    continue;
                }

                modes[i].sourceMode.position.x -= offsetX;
                modes[i].sourceMode.position.y -= offsetY;
            }

            var result = SetDisplayConfig(
                (uint)paths.Length,
                paths,
                (uint)modes.Length,
                modes,
                SdcApply | SdcUseSuppliedDisplayConfig | SdcAllowChanges | SdcSaveToDatabase);

            if (result != ErrorSuccess)
            {
                error = $"SetDisplayConfig failed (Win32 error {result}).";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TrySetPrimaryViaChangeDisplaySettings(MonitorInfo target, IReadOnlyList<MonitorInfo> monitors, out string error)
    {
        error = string.Empty;
        var offsetX = target.PositionX;
        var offsetY = target.PositionY;

        foreach (var monitor in monitors)
        {
            var devMode = CreateDevMode();
            if (!EnumDisplaySettings(monitor.DeviceName, EnumCurrentSettings, ref devMode))
            {
                error = $"Could not read settings for {monitor.DeviceName}.";
                return false;
            }

            devMode.dmPositionX = monitor.PositionX - offsetX;
            devMode.dmPositionY = monitor.PositionY - offsetY;
            devMode.dmFields = DmPosition;

            var flags = CdsUpdateRegistry | CdsNoReset;
            if (string.Equals(monitor.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                flags |= CdsSetPrimary;
            }

            var result = ChangeDisplaySettingsEx(monitor.DeviceName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
            if (result != DispChangeSuccessful)
            {
                error = $"ChangeDisplaySettingsEx staging failed for {monitor.DeviceName} (result {result}).";
                return false;
            }
        }

        var applyResult = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (applyResult != DispChangeSuccessful)
        {
            error = $"ChangeDisplaySettingsEx apply failed (result {applyResult}).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Polls for the primary flag instead of a single fixed sleep: returns as
    /// soon as the driver reports the change (usually well under 100 ms), and
    /// gives slow drivers up to ~1 s before declaring failure.
    /// </summary>
    private static bool VerifyPrimary(string deviceName)
    {
        const int attempts = 10;
        const int delayMs = 100;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (IsPrimaryNow(deviceName))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return IsPrimaryNow(deviceName);
    }

    private static bool IsPrimaryNow(string deviceName)
    {
        for (uint deviceIndex = 0; ; deviceIndex++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, deviceIndex, ref device, 0))
            {
                break;
            }

            if (!string.Equals(device.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return (device.StateFlags & DisplayDevicePrimaryDevice) != 0;
        }

        return false;
    }

    private static DEVMODE CreateDevMode() => new()
    {
        dmDeviceName = string.Empty,
        dmFormName = string.Empty,
        dmSize = (ushort)Marshal.SizeOf<DEVMODE>()
    };

    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) QueryActiveConfig()
    {
        var result = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount);
        if (result != ErrorSuccess)
        {
            throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed (Win32 error {result}).");
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        result = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (result != ErrorSuccess)
        {
            throw new InvalidOperationException($"QueryDisplayConfig failed (Win32 error {result}).");
        }

        Array.Resize(ref paths, (int)pathCount);
        Array.Resize(ref modes, (int)modeCount);
        return (paths, modes);
    }

    private readonly record struct DisplayBounds(int X, int Y, int Width, int Height);

    /// <summary>
    /// Maps GDI device names to virtual-desktop bounds via DisplayConfig. Best-effort:
    /// returns an empty map on failure so callers can fall back to DEVMODE.
    /// </summary>
    /// <summary>
    /// Maps GDI device names to friendly monitor names and virtual-desktop bounds
    /// via the DisplayConfig API. Best-effort: returns empty maps on any failure.
    /// Combined to reduce expensive Win32 API calls (QueryDisplayConfig).
    /// </summary>
    private static (Dictionary<string, string> FriendlyNames, Dictionary<string, DisplayBounds> Bounds) GetDisplayConfigDetails()
    {
        var friendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bounds = new Dictionary<string, DisplayBounds>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var (paths, modes) = QueryActiveConfig();

            foreach (var path in paths)
            {
                if ((path.flags & DisplayconfigPathActive) == 0)
                {
                    continue;
                }

                var sourceRequest = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetSourceName,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref sourceRequest) != ErrorSuccess ||
                    string.IsNullOrWhiteSpace(sourceRequest.viewGdiDeviceName))
                {
                    continue;
                }

                var gdiDeviceName = sourceRequest.viewGdiDeviceName;

                // 1. Get Friendly Name
                var targetRequest = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref targetRequest) == ErrorSuccess)
                {
                    friendlyNames[gdiDeviceName] = targetRequest.monitorFriendlyDeviceName;
                }

                // 2. Get Bounds
                var modeIdx = path.sourceInfo.modeInfoIdx;
                if (modeIdx < modes.Length)
                {
                    ref var mode = ref modes[modeIdx];
                    if (mode.infoType == DISPLAYCONFIG_MODE_INFO.TypeSource)
                    {
                        bounds[gdiDeviceName] = new DisplayBounds(
                            mode.sourceMode.position.x,
                            mode.sourceMode.position.y,
                            (int)mode.sourceMode.width,
                            (int)mode.sourceMode.height);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"DisplayConfig details lookup failed: {ex.Message}");
        }

        return (friendlyNames, bounds);
    }

    // ---- HDR / advanced color ----

    /// <summary>Per-monitor HDR capability and current state.</summary>
    public sealed record HdrStatus(bool Supported, bool Enabled);

    /// <summary>
    /// Queries HDR support/state for a monitor by GDI device name. Prefers the
    /// Win11 24H2 API (which separates HDR from WCG/auto color management) and
    /// falls back to the Win10 1709 advanced-color API. Returns null when the
    /// monitor cannot be resolved or the OS lacks both APIs.
    /// </summary>
    public HdrStatus? GetHdrStatus(string deviceName)
    {
        if (!TryFindTarget(deviceName, out var adapterId, out var targetId))
        {
            return null;
        }

        var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetAdvancedColorInfo2,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(),
                adapterId = adapterId,
                id = targetId,
            },
        };

        if (DisplayConfigGetDeviceInfo(ref info2) == ErrorSuccess)
        {
            return new HdrStatus(
                Supported: (info2.value & DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2.HighDynamicRangeSupported) != 0,
                Enabled: (info2.value & DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2.HighDynamicRangeUserEnabled) != 0);
        }

        var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetAdvancedColorInfo,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                adapterId = adapterId,
                id = targetId,
            },
        };

        if (DisplayConfigGetDeviceInfo(ref info) == ErrorSuccess)
        {
            var forceDisabled = (info.value & DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO.AdvancedColorForceDisabled) != 0;
            return new HdrStatus(
                Supported: (info.value & DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO.AdvancedColorSupported) != 0 && !forceDisabled,
                Enabled: (info.value & DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO.AdvancedColorEnabled) != 0);
        }

        return null;
    }

    /// <summary>
    /// Enables or disables HDR for a monitor by GDI device name. Tries the
    /// Win11 24H2 SET_HDR_STATE first, then the legacy advanced-color toggle.
    /// Throws on failure so callers can surface the error.
    /// </summary>
    public void SetHdrEnabled(string deviceName, bool enable)
    {
        if (!TryFindTarget(deviceName, out var adapterId, out var targetId))
        {
            throw new InvalidOperationException($"Monitor '{deviceName}' not found in the active display configuration.");
        }

        var hdrState = new DISPLAYCONFIG_SET_HDR_STATE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.SetHdrState,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_HDR_STATE>(),
                adapterId = adapterId,
                id = targetId,
            },
            value = enable ? DISPLAYCONFIG_SET_HDR_STATE.EnableHdr : 0u,
        };

        var result = DisplayConfigSetDeviceInfo(ref hdrState);
        if (result == ErrorSuccess)
        {
            AppLogger.Log($"HDR {(enable ? "enabled" : "disabled")} on {deviceName} (SET_HDR_STATE).");
            return;
        }

        // Pre-24H2: fall back to the legacy advanced-color toggle.
        var legacyState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.SetAdvancedColorState,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(),
                adapterId = adapterId,
                id = targetId,
            },
            value = enable ? DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE.EnableAdvancedColor : 0u,
        };

        var legacyResult = DisplayConfigSetDeviceInfo(ref legacyState);
        if (legacyResult == ErrorSuccess)
        {
            AppLogger.Log($"HDR {(enable ? "enabled" : "disabled")} on {deviceName} (SET_ADVANCED_COLOR_STATE fallback).");
            return;
        }

        throw new InvalidOperationException(
            $"Could not change HDR on '{deviceName}': SET_HDR_STATE error {result}, " +
            $"SET_ADVANCED_COLOR_STATE error {legacyResult}.");
    }

    /// <summary>
    /// Resolves a GDI device name (\\.\DISPLAYn) to the DisplayConfig target
    /// (adapter LUID + target id) needed for device-info calls.
    /// </summary>
    private static bool TryFindTarget(string deviceName, out LUID adapterId, out uint targetId)
    {
        adapterId = default;
        targetId = 0;

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        try
        {
            var (paths, _) = QueryActiveConfig();
            foreach (var path in paths)
            {
                if ((path.flags & DisplayconfigPathActive) == 0)
                {
                    continue;
                }

                var sourceRequest = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetSourceName,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                };

                if (DisplayConfigGetDeviceInfo(ref sourceRequest) != ErrorSuccess)
                {
                    continue;
                }

                if (string.Equals(sourceRequest.viewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    adapterId = path.targetInfo.adapterId;
                    targetId = path.targetInfo.id;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"HDR target lookup failed for {deviceName}: {ex.Message}");
        }

        return false;
    }
}
