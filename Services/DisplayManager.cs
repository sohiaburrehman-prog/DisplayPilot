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
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
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
        if (monitors.Count <= 1)
        {
            throw new InvalidOperationException("Only one monitor is connected — nothing to swap.");
        }

        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));
        }

        var target = monitors[monitorIndex];
        if (target.IsPrimary)
        {
            return target;
        }

        string? lastError = null;

        if (TrySetPrimaryViaDisplayConfig(target, monitors, out var displayConfigError))
        {
            if (VerifyPrimary(target.DeviceName))
            {
                AppLogger.Log($"Primary display set via DisplayConfig to {target.Name} ({target.DeviceName}).");

                // ⚡ Bolt: Construct and return the new primary monitor state directly to avoid a redundant,
                // expensive Win32 GetMonitors() call just to fetch the updated state.
                return new MonitorInfo
                {
                    Index = target.Index,
                    DeviceName = target.DeviceName,
                    Name = target.Name,
                    Width = target.Width,
                    Height = target.Height,
                    PositionX = 0,
                    PositionY = 0,
                    IsPrimary = true,
                    RefreshRateHz = target.RefreshRateHz
                };
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

                // ⚡ Bolt: Construct and return the new primary monitor state directly to avoid a redundant,
                // expensive Win32 GetMonitors() call just to fetch the updated state.
                return new MonitorInfo
                {
                    Index = target.Index,
                    DeviceName = target.DeviceName,
                    Name = target.Name,
                    Width = target.Width,
                    Height = target.Height,
                    PositionX = 0,
                    PositionY = 0,
                    IsPrimary = true,
                    RefreshRateHz = target.RefreshRateHz
                };
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

    /// <summary>With exactly two monitors, makes the non-primary one primary. Returns the new primary.</summary>
    public MonitorInfo SwapPrimaryBetweenTwoMonitors()
    {
        var monitors = GetMonitors();
        if (monitors.Count != 2)
        {
            throw new InvalidOperationException("Quick swap is only available with exactly two monitors.");
        }

        var nonPrimary = monitors.First(m => !m.IsPrimary);
        return SetPrimaryMonitor(nonPrimary.Index);
    }

    /// <summary>
    /// Makes the monitor with the given GDI device name primary. Used by
    /// auto-swap profiles. Throws if the device is not currently connected.
    /// </summary>
    public MonitorInfo SetPrimaryByDeviceName(string deviceName)
    {
        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            throw new InvalidOperationException($"Display {deviceName} is not currently connected.");
        }

        return SetPrimaryMonitor(target.Index);
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
        return SetPrimaryMonitor(next);
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
    /// Applies a resolution + refresh-rate change to one monitor. Validates with
    /// CDS_TEST first, then commits with CDS_UPDATEREGISTRY. Throws with a
    /// descriptive message on failure (leaving the current mode untouched).
    /// </summary>
    public void ApplyDisplayMode(string deviceName, DisplayMode mode)
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
}
