using System.Diagnostics;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Live state for the profile currently matched by the process watcher.</summary>
public sealed class ActiveProfileState
{
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileLabel { get; init; } = string.Empty;
    public string TargetMonitorLabel { get; init; } = string.Empty;
}

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

    private readonly Dictionary<string, LauncherChildTracker.WatchState> _launcherWatch = new();
    private readonly Dictionary<string, bool> _launcherRunningState = new();

    private System.Threading.Timer? _timer;
    private bool _polling;
    private bool _disposed;
    private ActiveProfileState? _currentActiveProfile;

    public event EventHandler? PrimaryChanged;

    /// <summary>Raised when the matched active profile changes (including cleared).</summary>
    public event EventHandler? ActiveProfileChanged;

    /// <summary>Brief user-facing status (tray/panel); throttled by subscribers.</summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>Profile currently matched by the watcher, if any.</summary>
    public ActiveProfileState? CurrentActiveProfile
    {
        get
        {
            lock (_lock)
            {
                return _currentActiveProfile;
            }
        }
    }

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
                _launcherWatch.Clear();
                _launcherRunningState.Clear();
                SetCurrentActiveProfile(null);
                return;
            }

            var runningNames = GetRunningProcessNames();
            var detectedChildren = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in profiles)
            {
                if (!LauncherChildTracker.IsLauncherProfile(profile))
                {
                    _launcherWatch.Remove(profile.Id);
                    _launcherRunningState.Remove(profile.Id);
                    continue;
                }

                var launcherRunning = runningNames.Contains(profile.NormalizedProcessName);
                var launcherWasRunning = _launcherRunningState.TryGetValue(profile.Id, out var was) && was;
                _launcherRunningState[profile.Id] = launcherRunning;

                if (!_launcherWatch.TryGetValue(profile.Id, out var watchState))
                {
                    watchState = new LauncherChildTracker.WatchState();
                    _launcherWatch[profile.Id] = watchState;
                }

                var child = LauncherChildTracker.UpdateWatchState(
                    profile, watchState, runningNames, launcherWasRunning, launcherRunning);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    detectedChildren[profile.Id] = child;
                }
            }

            foreach (var profile in profiles)
            {
                detectedChildren.TryGetValue(profile.Id, out var detectedChild);
                var isRunning = ProfileMatcher.IsProfileActive(profile, runningNames, detectedChild);
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

            UpdateCurrentActiveProfile(profiles, runningNames, detectedChildren);

            // Forget state for profiles that no longer exist.
            var liveIds = profiles.Select(p => p.Id).ToHashSet();
            foreach (var staleId in _runningState.Keys.Where(k => !liveIds.Contains(k)).ToList())
            {
                _runningState.Remove(staleId);
                _previousPrimary.Remove(staleId);
                _launcherWatch.Remove(staleId);
                _launcherRunningState.Remove(staleId);
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
            var label = profile.DisplayLabel;
            var monitors = _displayManager.GetMonitors();
            var target = ProfileMatcher.ResolveTarget(profile, monitors);
            if (target is null)
            {
                AppLogger.Log(
                    $"Profile activate skipped [{label}]: target '{profile.TargetMonitorName}' " +
                    $"(device {profile.TargetMonitorDeviceName}) not among {monitors.Count} connected display(s).");
                return;
            }

            if (target.IsPrimary)
            {
                AppLogger.Log($"Profile skip [{label}]: '{target.Name}' ({target.DeviceName}) is already primary.");
                return;
            }

            var currentPrimary = monitors.FirstOrDefault(m => m.IsPrimary);
            if (currentPrimary is not null)
            {
                _previousPrimary[profile.Id] = currentPrimary.DeviceName;
                AppLogger.Log(
                    $"Profile activate [{label}]: process started; saving previous primary " +
                    $"'{currentPrimary.Name}' ({currentPrimary.DeviceName}).");
            }
            else
            {
                AppLogger.Log($"Profile activate [{label}]: process started; no previous primary recorded.");
            }

            _displayManager.SetPrimaryByDeviceName(target.DeviceName);
            var displayName = MonitorDisplayHelper.GetDisplayName(target, _settings.Current);
            RecordProfileTriggered(profile);
            AppLogger.Log(
                $"Profile activated [{label}]: primary set to '{displayName}' ({target.DeviceName}).");
            StatusMessage?.Invoke(this, $"Auto-swap: {displayName} is now primary.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.LogException($"ProcessWatcher start '{profile.DisplayLabel}'", ex);
        }
    }

    private void OnProcessExited(AppProfile profile)
    {
        try
        {
            var label = profile.DisplayLabel;
            if (!profile.RestoreOnExit)
            {
                _previousPrimary.Remove(profile.Id);
                AppLogger.Log($"Profile exit [{label}]: restore-on-exit disabled; leaving primary unchanged.");
                return;
            }

            if (!_previousPrimary.TryGetValue(profile.Id, out var previousDevice) ||
                string.IsNullOrWhiteSpace(previousDevice))
            {
                AppLogger.Log($"Profile exit [{label}]: no saved previous primary to restore.");
                return;
            }

            _previousPrimary.Remove(profile.Id);

            var monitors = _displayManager.GetMonitors();
            var previous = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, previousDevice, StringComparison.OrdinalIgnoreCase));

            if (previous is null)
            {
                AppLogger.Log(
                    $"Profile restore skipped [{label}]: previous primary device '{previousDevice}' " +
                    $"not among {monitors.Count} connected display(s).");
                return;
            }

            if (previous.IsPrimary)
            {
                AppLogger.Log($"Profile restore skip [{label}]: '{previous.Name}' is already primary.");
                return;
            }

            _displayManager.SetPrimaryByDeviceName(previous.DeviceName);
            var displayName = MonitorDisplayHelper.GetDisplayName(previous, _settings.Current);
            AppLogger.Log(
                $"Profile restored [{label}]: primary returned to '{displayName}' ({previous.DeviceName}).");
            StatusMessage?.Invoke(this, $"Auto-swap: restored {displayName} as primary.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLogger.LogException($"ProcessWatcher exit '{profile.DisplayLabel}'", ex);
        }
    }

    private void RecordProfileTriggered(AppProfile profile)
    {
        var now = DateTime.UtcNow;
        _settings.Update(s =>
        {
            s.LastUsedProfileId = profile.Id;
            var live = s.Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (live is not null)
            {
                live.LastTriggeredUtc = now;
            }
        });
    }

    private void UpdateCurrentActiveProfile(
        IReadOnlyList<AppProfile> profiles,
        HashSet<string> runningNames,
        Dictionary<string, string> detectedChildren)
    {
        AppProfile? active = null;
        foreach (var profile in profiles)
        {
            detectedChildren.TryGetValue(profile.Id, out var detectedChild);
            if (ProfileMatcher.IsProfileActive(profile, runningNames, detectedChild))
            {
                active = profile;
                break;
            }
        }

        if (active is null)
        {
            SetCurrentActiveProfile(null);
            return;
        }

        // ⚡ Bolt Optimization:
        // Avoid calling _displayManager.GetMonitors() on every poll cycle if the active profile
        // hasn't changed. GetMonitors() executes expensive Win32 GPU queries (QueryDisplayConfig)
        // which can cause micro-stutters.
        var current = CurrentActiveProfile;
        if (current != null && current.ProfileId == active.Id)
        {
            SetCurrentActiveProfile(new ActiveProfileState
            {
                ProfileId = active.Id,
                ProfileLabel = active.DisplayLabel,
                TargetMonitorLabel = current.TargetMonitorLabel,
            });
            return;
        }

        var monitors = _displayManager.GetMonitors();
        var target = ProfileMatcher.ResolveTarget(active, monitors);
        var monitorLabel = target is not null
            ? MonitorDisplayHelper.GetDisplayName(target, _settings.Current)
            : active.TargetMonitorName;

        SetCurrentActiveProfile(new ActiveProfileState
        {
            ProfileId = active.Id,
            ProfileLabel = active.DisplayLabel,
            TargetMonitorLabel = monitorLabel,
        });
    }

    private void SetCurrentActiveProfile(ActiveProfileState? state)
    {
        lock (_lock)
        {
            if (_currentActiveProfile?.ProfileId == state?.ProfileId &&
                _currentActiveProfile?.ProfileLabel == state?.ProfileLabel &&
                _currentActiveProfile?.TargetMonitorLabel == state?.TargetMonitorLabel)
            {
                return;
            }

            _currentActiveProfile = state;
        }

        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    public static HashSet<string> GetRunningProcessNames()
    {
        var processes = Process.GetProcesses();
        var names = new HashSet<string>(processes.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < processes.Length; i++)
        {
            var process = processes[i];
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
