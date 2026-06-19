using System.Diagnostics;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Watches running processes on a timer and applies auto-swap profiles: when a
/// matched process starts, the configured monitor becomes primary; when it
/// exits, the previous primary is optionally restored. Robust to monitors that
/// are not currently connected (skipped and logged).
/// </summary>
public sealed class ProcessWatcherService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly DisplayManager _displayManager;
    private readonly object _lock = new();

    // Per-profile id: was the process running at the last poll?
    private readonly Dictionary<string, bool> _runningState = new();

    // Per-profile id: the primary device name captured when we activated it, so
    // we can restore it on exit.
    private readonly Dictionary<string, string> _previousPrimary = new();

    private System.Threading.Timer? _timer;
    private bool _polling;
    private bool _disposed;

    public event EventHandler? PrimaryChanged;

    public ProcessWatcherService(SettingsService settings, DisplayManager displayManager)
    {
        _settings = settings;
        _displayManager = displayManager;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var interval = Math.Max(1000, _settings.Current.ProcessWatchIntervalMs);
            _timer ??= new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(interval, interval);
            AppLogger.Log($"Process watcher started (interval {interval} ms).");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Re-reads the poll interval after a settings change.</summary>
    public void Reconfigure()
    {
        lock (_lock)
        {
            if (_timer is null || _disposed)
            {
                return;
            }

            var interval = Math.Max(1000, _settings.Current.ProcessWatchIntervalMs);
            _timer.Change(interval, interval);
        }
    }

    private void Poll()
    {
        // Skip if a previous poll is still running (display changes can block).
        lock (_lock)
        {
            if (_polling || _disposed)
            {
                return;
            }

            _polling = true;
        }

        try
        {
            var profiles = _settings.Current.Profiles
                .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .ToList();

            if (profiles.Count == 0)
            {
                _runningState.Clear();
                _previousPrimary.Clear();
                return;
            }

            var runningNames = GetRunningProcessNames();

            foreach (var profile in profiles)
            {
                var isRunning = runningNames.Contains(profile.NormalizedProcessName);
                var wasRunning = _runningState.TryGetValue(profile.Id, out var prev) && prev;
                _runningState[profile.Id] = isRunning;

                if (isRunning && !wasRunning)
                {
                    OnProcessStarted(profile);
                }
                else if (!isRunning && wasRunning)
                {
                    OnProcessExited(profile);
                }
            }

            // Forget state for profiles that no longer exist.
            var liveIds = profiles.Select(p => p.Id).ToHashSet();
            foreach (var staleId in _runningState.Keys.Where(k => !liveIds.Contains(k)).ToList())
            {
                _runningState.Remove(staleId);
                _previousPrimary.Remove(staleId);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogException("ProcessWatcher.Poll", ex);
        }
        finally
        {
            lock (_lock)
            {
                _polling = false;
            }
        }
    }

    private void OnProcessStarted(AppProfile profile)
    {
        try
        {
            var monitors = _displayManager.GetMonitors();
            var target = ProfileMatcher.ResolveTarget(profile, monitors);
            if (target is null)
            {
                AppLogger.Log($"Profile '{profile.ProcessName}': target display '{profile.TargetMonitorName}' not connected; skipping.");
                return;
            }

            if (target.IsPrimary)
            {
                AppLogger.Log($"Profile '{profile.ProcessName}': '{target.Name}' is already primary.");
                return;
            }

            var currentPrimary = monitors.FirstOrDefault(m => m.IsPrimary);
            if (currentPrimary is not null)
            {
                _previousPrimary[profile.Id] = currentPrimary.DeviceName;
            }

            _displayManager.SetPrimaryByDeviceName(target.DeviceName);
            AppLogger.Log($"Profile '{profile.ProcessName}': set primary to '{target.Name}'.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.LogException($"ProcessWatcher start '{profile.ProcessName}'", ex);
        }
    }

    private void OnProcessExited(AppProfile profile)
    {
        try
        {
            if (!profile.RestoreOnExit)
            {
                _previousPrimary.Remove(profile.Id);
                return;
            }

            if (!_previousPrimary.TryGetValue(profile.Id, out var previousDevice) ||
                string.IsNullOrWhiteSpace(previousDevice))
            {
                return;
            }

            _previousPrimary.Remove(profile.Id);

            var monitors = _displayManager.GetMonitors();
            var previous = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, previousDevice, StringComparison.OrdinalIgnoreCase));

            if (previous is null)
            {
                AppLogger.Log($"Profile '{profile.ProcessName}': previous primary no longer connected; skipping restore.");
                return;
            }

            if (previous.IsPrimary)
            {
                return;
            }

            _displayManager.SetPrimaryByDeviceName(previous.DeviceName);
            AppLogger.Log($"Profile '{profile.ProcessName}': restored primary to '{previous.Name}'.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.LogException($"ProcessWatcher exit '{profile.ProcessName}'", ex);
        }
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName);
            }
            catch
            {
                // Process exited between enumeration and read; ignore.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
