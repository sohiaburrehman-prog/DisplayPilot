using System.Diagnostics;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Native;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Post-swap window rescue for auto-swap profiles. Many games decide which
/// display to use during engine init — within the first few hundred ms of the
/// process starting — so even a ~1 s WMI-triggered primary swap can lose the
/// race and the game window stays on the old primary. This service waits for
/// the matched process's main window to appear and settle, then moves it to
/// the profile's target monitor (the automated equivalent of Win+Shift+Arrow).
///
/// Best effort by design: exclusive-fullscreen titles that re-assert their
/// adapter can snap back; one retry is attempted, then we log and give up.
/// </summary>
public sealed class WindowRelocationService
{
    private readonly DisplayManager _displayManager;
    private readonly object _lock = new();

    // Profile ids with a relocation task in flight, so a rapid exit/restart
    // doesn't stack watchers for the same profile.
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    /// <summary>Total time to wait for the game's main window to appear.</summary>
    private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Recheck delay after a move, in case the game re-asserts its display.</summary>
    private static readonly TimeSpan PostMoveRecheck = TimeSpan.FromSeconds(3);

    // Ignore splash screens and utility popups.
    private const int MinWindowWidth = 320;
    private const int MinWindowHeight = 240;

    /// <summary>Brief user-facing status (tray/panel); throttled by subscribers.</summary>
    public event EventHandler<string>? StatusMessage;

    public WindowRelocationService(DisplayManager displayManager)
    {
        _displayManager = displayManager;
    }

    /// <summary>
    /// Starts a background watch that moves the profile's game window onto the
    /// target monitor once it appears. No-op when a watch for this profile is
    /// already running or no concrete game exe can be determined.
    /// </summary>
    /// <param name="profile">The activated profile (snapshot/clone is fine).</param>
    /// <param name="detectedLauncherChild">Game exe detected by the launcher child
    /// tracker this poll, if any.</param>
    public void BeginWatch(AppProfile profile, string? detectedLauncherChild)
    {
        if (profile is null || !profile.MoveWindowToTarget)
        {
            return;
        }

        var candidates = GetWindowProcessCandidates(profile, detectedLauncherChild);
        if (candidates.Count == 0)
        {
            AppLogger.Log(
                $"Window rescue skipped [{profile.DisplayLabel}]: no game exe to watch " +
                "(launcher profile without resolved target or detected child).");
            return;
        }

        lock (_lock)
        {
            if (!_active.Add(profile.Id))
            {
                return;
            }
        }

        var snapshot = profile.Clone();
        _ = Task.Run(async () =>
        {
            try
            {
                await WatchAndRelocateAsync(snapshot, candidates).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.LogException($"Window rescue '{snapshot.DisplayLabel}'", ex);
            }
            finally
            {
                lock (_lock)
                {
                    _active.Remove(snapshot.Id);
                }
            }
        });
    }

    /// <summary>
    /// The exe names whose window we are allowed to move. Launcher windows are
    /// never moved — only the game itself.
    /// </summary>
    internal static List<string> GetWindowProcessCandidates(AppProfile profile, string? detectedLauncherChild)
    {
        var names = new List<string>(2);

        if (profile.HasResolvedTarget)
        {
            names.Add(profile.NormalizedResolvedTarget);
        }

        if (!string.IsNullOrWhiteSpace(detectedLauncherChild))
        {
            var child = Normalize(detectedLauncherChild);
            if (!names.Contains(child, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(child);
            }
        }

        if (names.Count == 0 &&
            !LauncherCatalog.IsKnownLauncher(profile.ProcessName) &&
            !string.IsNullOrEmpty(profile.NormalizedProcessName))
        {
            names.Add(profile.NormalizedProcessName);
        }

        return names;
    }

    private async Task WatchAndRelocateAsync(AppProfile profile, List<string> processNames)
    {
        var label = profile.DisplayLabel;
        AppLogger.Log($"Window rescue [{label}]: waiting for main window of '{string.Join("' / '", processNames)}'.");

        var deadline = DateTime.UtcNow + WindowWaitTimeout;
        var lastHwnd = IntPtr.Zero;
        var lastRect = default(WindowInterop.RECT);
        var stable = false;

        while (DateTime.UtcNow < deadline)
        {
            var hwnd = FindBestWindow(processNames, out var rect);
            if (hwnd != IntPtr.Zero)
            {
                // Require the same window with the same bounds on two consecutive
                // polls — games create, resize, and recreate windows during init.
                if (hwnd == lastHwnd &&
                    rect.Left == lastRect.Left && rect.Top == lastRect.Top &&
                    rect.Width == lastRect.Width && rect.Height == lastRect.Height)
                {
                    stable = true;
                    break;
                }

                lastHwnd = hwnd;
                lastRect = rect;
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        if (!stable)
        {
            AppLogger.Log($"Window rescue [{label}]: no settled game window within {WindowWaitTimeout.TotalSeconds:0} s; giving up.");
            return;
        }

        if (!RelocateIfNeeded(profile, lastHwnd, firstAttempt: true))
        {
            return;
        }

        // Some titles re-assert their display shortly after; check once more.
        await Task.Delay(PostMoveRecheck).ConfigureAwait(false);
        if (WindowInterop.IsWindow(lastHwnd))
        {
            RelocateIfNeeded(profile, lastHwnd, firstAttempt: false);
        }
    }

    /// <summary>
    /// Moves the window onto the profile's target monitor when it is elsewhere.
    /// Returns true when a move was performed (so a recheck is worthwhile).
    /// </summary>
    private bool RelocateIfNeeded(AppProfile profile, IntPtr hwnd, bool firstAttempt)
    {
        var label = profile.DisplayLabel;
        var monitors = _displayManager.GetMonitors();
        var target = ProfileMatcher.ResolveTarget(profile, monitors);
        if (target is null)
        {
            AppLogger.Log($"Window rescue skipped [{label}]: target display not connected.");
            return false;
        }

        if (!WindowInterop.GetWindowRect(hwnd, out var rect))
        {
            AppLogger.Log($"Window rescue [{label}]: GetWindowRect failed; window may have closed.");
            return false;
        }

        if (IsOnMonitor(rect, target))
        {
            AppLogger.Log(firstAttempt
                ? $"Window rescue [{label}]: game window already on '{target.Name}'; nothing to do."
                : $"Window rescue [{label}]: window stayed on '{target.Name}' after move.");
            return false;
        }

        var current = MonitorContaining(rect, monitors);
        var maximized = WindowInterop.IsZoomed(hwnd);
        var style = WindowInterop.GetWindowStyle(hwnd);
        var borderless = (style & WindowInterop.WS_CAPTION) != WindowInterop.WS_CAPTION;
        var fullscreenSized = current is not null &&
                              rect.Width >= (int)(current.Width * 0.98) &&
                              rect.Height >= (int)(current.Height * 0.98);

        bool moved;
        if (maximized)
        {
            // Restore → move → re-maximize so the window maximizes on the target.
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_RESTORE);
            moved = MoveCentered(hwnd, rect, target);
            WindowInterop.ShowWindow(hwnd, WindowInterop.SW_MAXIMIZE);
        }
        else if (borderless || fullscreenSized)
        {
            // Borderless/fullscreen: cover the target monitor exactly.
            moved = WindowInterop.SetWindowPos(
                hwnd, IntPtr.Zero,
                target.PositionX, target.PositionY, target.Width, target.Height,
                WindowInterop.SWP_NOZORDER | WindowInterop.SWP_FRAMECHANGED);
        }
        else
        {
            moved = MoveCentered(hwnd, rect, target);
        }

        if (moved)
        {
            WindowInterop.SetForegroundWindow(hwnd);
            var displayName = target.Name;
            AppLogger.Log(
                $"Window rescue [{label}]: moved game window " +
                $"({rect.Width}x{rect.Height} at {rect.Left},{rect.Top}) to '{displayName}' " +
                $"({target.PositionX},{target.PositionY} {target.Width}x{target.Height})" +
                $"{(borderless || fullscreenSized ? " fullscreen-sized" : maximized ? " maximized" : "")}.");
            if (firstAttempt)
            {
                StatusMessage?.Invoke(this, $"Auto-swap: moved game window to {displayName}.");
            }
        }
        else
        {
            AppLogger.Log($"Window rescue [{label}]: SetWindowPos failed (exclusive fullscreen or protected window?).");
        }

        return moved;
    }

    private static bool MoveCentered(IntPtr hwnd, WindowInterop.RECT rect, MonitorInfo target)
    {
        var width = Math.Min(rect.Width, target.Width);
        var height = Math.Min(rect.Height, target.Height);
        var x = target.PositionX + Math.Max(0, (target.Width - width) / 2);
        var y = target.PositionY + Math.Max(0, (target.Height - height) / 2);
        return WindowInterop.SetWindowPos(
            hwnd, IntPtr.Zero, x, y, width, height,
            WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    private static bool IsOnMonitor(WindowInterop.RECT rect, MonitorInfo monitor) =>
        rect.CenterX >= monitor.PositionX && rect.CenterX < monitor.PositionX + monitor.Width &&
        rect.CenterY >= monitor.PositionY && rect.CenterY < monitor.PositionY + monitor.Height;

    private static MonitorInfo? MonitorContaining(WindowInterop.RECT rect, IReadOnlyList<MonitorInfo> monitors) =>
        monitors.FirstOrDefault(m => IsOnMonitor(rect, m));

    /// <summary>
    /// Finds the largest visible, non-cloaked, non-minimized top-level window
    /// belonging to any process with one of the given names.
    /// </summary>
    private static IntPtr FindBestWindow(List<string> processNames, out WindowInterop.RECT bestRect)
    {
        var pids = new HashSet<uint>();
        foreach (var name in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    pids.Add((uint)process.Id);
                }
                catch
                {
                    // Exited between enumeration and read; ignore.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        var best = IntPtr.Zero;
        var rect = default(WindowInterop.RECT);
        long bestArea = 0;

        if (pids.Count > 0)
        {
            WindowInterop.EnumWindows((hwnd, _) =>
            {
                WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
                if (!pids.Contains(pid) ||
                    !WindowInterop.IsWindowVisible(hwnd) ||
                    WindowInterop.IsIconic(hwnd) ||
                    WindowInterop.IsCloaked(hwnd) ||
                    !WindowInterop.GetWindowRect(hwnd, out var r) ||
                    r.Width < MinWindowWidth || r.Height < MinWindowHeight)
                {
                    return true;
                }

                var area = (long)r.Width * r.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                    rect = r;
                }

                return true;
            }, IntPtr.Zero);
        }

        bestRect = rect;
        return best;
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }
}
