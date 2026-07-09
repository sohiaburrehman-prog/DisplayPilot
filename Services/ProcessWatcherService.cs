using System.Diagnostics;
using System.Management;

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

    // Reused collections for the high-frequency polling loop to eliminate Gen0 GC allocations
    private readonly List<AppProfile> _activeProfiles = new();
    private readonly HashSet<string> _runningNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _detectedChildren = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _liveIds = new();
    private readonly List<string> _staleIds = new();

    private System.Threading.Timer? _timer;
    private ManagementEventWatcher? _startWatcher;
    private bool _polling;
    private bool _disposed;
    private ActiveProfileState? _currentActiveProfile;

    // Fallback timer cadence once WMI push-detection is active. WMI catches
    // process starts within ~1 s, so the timer only needs to cover exits and
    // act as a safety net if WMI is unavailable.
    private int PollIntervalMs => Math.Max(1000, _settings.Current.ProcessWatchIntervalMs);

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

            var interval = PollIntervalMs;
            _timer ??= new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(interval, interval);
            AppLogger.Log($"Process watcher started (timer interval {interval} ms).");
        }

        // Push-based detection: WMI notifies us the moment any process starts,
        // so a matched game triggers the swap in ~1 s instead of up to a full
        // poll interval. Runs outside the lock (WMI setup can block briefly).
        StartProcessCreationWatcher();
    }

    /// <summary>
    /// Subscribes to WMI process-creation events. On any new process we kick an
    /// immediate poll, which runs the normal matching logic right away. Best
    /// effort: if WMI is unavailable the timer poll still covers detection.
    /// </summary>
    private void StartProcessCreationWatcher()
    {
        lock (_lock)
        {
            if (_disposed || _startWatcher is not null)
            {
                return;
            }
        }

        try
        {
            // WITHIN 1 = WMI coalesces creation events on a 1-second window,
            // the lowest reliable value without elevation.
            var query = new WqlEventQuery(
                "__InstanceCreationEvent",
                TimeSpan.FromSeconds(1),
                "TargetInstance ISA 'Win32_Process'");

            var watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += OnProcessCreationEvent;
            watcher.Start();

            lock (_lock)
            {
                _startWatcher = watcher;
            }

            AppLogger.Log("Process-creation watcher active (WMI push detection).");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WMI process-creation watcher unavailable; timer polling only. {ex.Message}");
        }
    }

    private void OnProcessCreationEvent(object sender, EventArrivedEventArgs e)
    {
        // A new process appeared — run the matcher now rather than waiting for
        // the next timer tick. The _polling guard coalesces bursts (a game
        // launch spawns many processes) into non-overlapping polls.
        Poll();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        StopProcessCreationWatcher();
    }

    private void StopProcessCreationWatcher()
    {
        ManagementEventWatcher? watcher;
        lock (_lock)
        {
            watcher = _startWatcher;
            _startWatcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EventArrived -= OnProcessCreationEvent;
            watcher.Stop();
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WMI watcher stop error: {ex.Message}");
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
            _activeProfiles.Clear();
            foreach (var p in _settings.Current.Profiles)
            {
                if (p.Enabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                {
                    _activeProfiles.Add(p);
                }
            }

            if (_activeProfiles.Count == 0)
            {
                _runningState.Clear();
                _previousPrimary.Clear();
                _launcherWatch.Clear();
                _launcherRunningState.Clear();
                SetCurrentActiveProfile(null);
                return;
            }

            UpdateRunningProcessNames(_runningNames);
            _detectedChildren.Clear();

            foreach (var profile in _activeProfiles)
            {
                if (!LauncherChildTracker.IsLauncherProfile(profile))
                {
                    _launcherWatch.Remove(profile.Id);
                    _launcherRunningState.Remove(profile.Id);
                    continue;
                }

                var launcherRunning = _runningNames.Contains(profile.NormalizedProcessName);
                var launcherWasRunning = _launcherRunningState.TryGetValue(profile.Id, out var was) && was;
                _launcherRunningState[profile.Id] = launcherRunning;

                if (!_launcherWatch.TryGetValue(profile.Id, out var watchState))
                {
                    watchState = new LauncherChildTracker.WatchState();
                    _launcherWatch[profile.Id] = watchState;
                }

                var child = LauncherChildTracker.UpdateWatchState(
                    profile, watchState, _runningNames, launcherWasRunning, launcherRunning);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    _detectedChildren[profile.Id] = child;
                }
            }

            foreach (var profile in _activeProfiles)
            {
                _detectedChildren.TryGetValue(profile.Id, out var detectedChild);
                var isRunning = ProfileMatcher.IsProfileActive(profile, _runningNames, detectedChild);
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

            UpdateCurrentActiveProfile(_activeProfiles, _runningNames, _detectedChildren);

            // Forget state for profiles that no longer exist.
            _liveIds.Clear();
            foreach (var p in _activeProfiles)
            {
                _liveIds.Add(p.Id);
            }

            _staleIds.Clear();
            foreach (var k in _runningState.Keys)
            {
                if (!_liveIds.Contains(k))
                {
                    _staleIds.Add(k);
                }
            }

            foreach (var staleId in _staleIds)
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

        // Cache hit: if the same profile is still active, avoid the expensive Win32
        // GetMonitors() call (which queries DisplayConfig and causes micro-stutters
        // if called repeatedly on a timer).
        var current = CurrentActiveProfile;
        if (current is not null && current.ProfileId == active.Id)
        {
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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UpdateRunningProcessNames(names);
        return names;
    }

    public static void UpdateRunningProcessNames(HashSet<string> names)
    {
        names.Clear();
        var processes = Process.GetProcesses();
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
    }

    public void Dispose()
    {
        StopProcessCreationWatcher();

        lock (_lock)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
