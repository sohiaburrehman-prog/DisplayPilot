using System.Diagnostics;
using System.Management;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Live state for the profile currently winning conflict resolution.</summary>
public sealed class ActiveProfileState
{
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileLabel { get; init; } = string.Empty;
    public string TargetMonitorLabel { get; init; } = string.Empty;
}

/// <summary>
/// Watches processes and maintains one conflict-safe auto-swap session. When
/// several profiles match, a deterministic winner is selected. If that winner
/// exits or is disabled, the next eligible profile is promoted; when the final
/// winner exits, the original session primary is restored when requested.
/// </summary>
public sealed class ProcessWatcherService : IDisposable
{
    private static readonly TimeSpan ProcessStartRetention = TimeSpan.FromMinutes(10);

    private readonly SettingsService _settings;
    private readonly DisplayManager _displayManager;
    private readonly WindowRelocationService _windowRelocation;
    private readonly object _lock = new();

    private readonly Dictionary<string, bool> _runningState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _activationOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LauncherChildTracker.WatchState> _launcherWatch = new(StringComparer.Ordinal);
    private readonly List<LauncherChildTracker.ProcessStart> _recentProcessStarts = new();

    private System.Threading.Timer? _timer;
    private ManagementEventWatcher? _startWatcher;
    private bool _polling;
    private bool _disposed;
    private volatile bool _forceReconcile;
    private long _nextActivationOrder;
    private ActiveProfileState? _currentActiveProfile;

    private string? _sessionBaselinePrimaryDevice;
    private AppProfile? _winnerSnapshot;
    private string? _winnerDetectedLauncherChild;

    private int PollIntervalMs => Math.Max(1000, _settings.Current.ProcessWatchIntervalMs);

    public event EventHandler? PrimaryChanged;
    public event EventHandler? ActiveProfileChanged;
    public event EventHandler<string>? StatusMessage;

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
        _windowRelocation = new WindowRelocationService(displayManager);
        _windowRelocation.StatusMessage += (_, msg) => StatusMessage?.Invoke(this, msg);
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

        StartProcessCreationWatcher();
        ThreadPool.QueueUserWorkItem(_ => Poll());
    }

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

            AppLogger.Log("Process-creation watcher active (WMI PID ancestry detection).");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WMI process-creation watcher unavailable; timer polling only. {ex.Message}");
        }
    }

    private void OnProcessCreationEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is ManagementBaseObject process)
            {
                var name = LauncherCatalog.Normalize(Convert.ToString(process["Name"]) ?? string.Empty);
                var pid = Convert.ToUInt32(process["ProcessId"]);
                var parentPid = Convert.ToUInt32(process["ParentProcessId"]);
                if (pid != 0 && !string.IsNullOrWhiteSpace(name))
                {
                    lock (_lock)
                    {
                        _recentProcessStarts.RemoveAll(p => p.ProcessId == pid);
                        _recentProcessStarts.Add(new LauncherChildTracker.ProcessStart(
                            pid, parentPid, name, DateTime.UtcNow));
                        TrimRecentProcessStarts_NoLock();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not read WMI process ancestry: {ex.Message}");
        }

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

    /// <summary>Re-reads settings and reconciles active profiles immediately.</summary>
    public void Reconfigure()
    {
        _forceReconcile = true;
        lock (_lock)
        {
            if (_timer is null || _disposed)
            {
                return;
            }

            var interval = Math.Max(1000, _settings.Current.ProcessWatchIntervalMs);
            _timer.Change(interval, interval);
        }

        ThreadPool.QueueUserWorkItem(_ => Poll());
    }

    private void Poll()
    {
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
            var settings = _settings.Current;
            var profiles = settings.Profiles
                .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .Select(p => p.Clone())
                .ToList();
            var currentProcesses = GetRunningProcesses();
            var runningNames = currentProcesses
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<LauncherChildTracker.ProcessStart> recentStarts;
            lock (_lock)
            {
                TrimRecentProcessStarts_NoLock();
                recentStarts = _recentProcessStarts.ToList();
            }

            var candidates = new List<ProfileConflictResolver.Candidate>();
            foreach (var profile in profiles)
            {
                string? detectedChild = null;
                if (LauncherChildTracker.IsLauncherProfile(profile))
                {
                    if (!_launcherWatch.TryGetValue(profile.Id, out var watchState))
                    {
                        watchState = new LauncherChildTracker.WatchState();
                        _launcherWatch[profile.Id] = watchState;
                    }

                    detectedChild = LauncherChildTracker.UpdateWatchState(
                        profile, watchState, currentProcesses, recentStarts);
                }
                else
                {
                    _launcherWatch.Remove(profile.Id);
                }

                var isRunning = ProfileMatcher.IsProfileActive(profile, runningNames, detectedChild);
                var wasRunning = _runningState.TryGetValue(profile.Id, out var previous) && previous;
                _runningState[profile.Id] = isRunning;

                if (isRunning && !wasRunning)
                {
                    _activationOrder[profile.Id] = ++_nextActivationOrder;
                    AppLogger.Log($"Profile matched [{profile.DisplayLabel}] (priority {profile.Priority}).");
                }

                if (isRunning)
                {
                    if (!_activationOrder.TryGetValue(profile.Id, out var order))
                    {
                        order = ++_nextActivationOrder;
                        _activationOrder[profile.Id] = order;
                    }

                    candidates.Add(new ProfileConflictResolver.Candidate(profile, order, detectedChild));
                }
            }

            ReconcileWinner(candidates, settings.ProfileConflictRule);

            var liveIds = profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var staleId in _runningState.Keys.Where(k => !liveIds.Contains(k)).ToList())
            {
                _runningState.Remove(staleId);
                _activationOrder.Remove(staleId);
                _launcherWatch.Remove(staleId);
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

    private void ReconcileWinner(
        IReadOnlyList<ProfileConflictResolver.Candidate> activeCandidates,
        ProfileConflictRule rule)
    {
        var ordered = ProfileConflictResolver.OrderCandidates(activeCandidates, rule).ToList();
        var preferred = ordered.FirstOrDefault();

        if (preferred is not null &&
            !_forceReconcile &&
            _winnerSnapshot is not null &&
            preferred.Profile.Id == _winnerSnapshot.Id &&
            string.Equals(
                preferred.Profile.TargetMonitorDeviceName,
                _winnerSnapshot.TargetMonitorDeviceName,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(
                    preferred.DetectedLauncherChild,
                    _winnerDetectedLauncherChild,
                    StringComparison.OrdinalIgnoreCase))
            {
                _winnerDetectedLauncherChild = preferred.DetectedLauncherChild;
                _windowRelocation.BeginWatch(preferred.Profile, preferred.DetectedLauncherChild);
            }

            return;
        }

        // Fast-path: If no profile is active, and we weren't previously tracking one,
        // skip the expensive GetMonitors() (Win32 QueryDisplayConfig) system call.
        if (preferred is null && _winnerSnapshot is null)
        {
            _forceReconcile = false;
            SetCurrentActiveProfile(null);
            return;
        }

        var monitors = _displayManager.GetMonitors();
        _forceReconcile = false;
        ProfileConflictResolver.Candidate? winner = null;
        MonitorInfo? target = null;
        foreach (var candidate in ordered)
        {
            target = ProfileMatcher.ResolveTarget(candidate.Profile, monitors);
            if (target is not null)
            {
                winner = candidate;
                break;
            }

            AppLogger.Log(
                $"Profile conflict candidate skipped [{candidate.Profile.DisplayLabel}]: " +
                $"target display is not connected.");
        }

        if (winner is null || target is null)
        {
            EndAutomationSession(monitors);
            return;
        }

        if (_winnerSnapshot is null)
        {
            _sessionBaselinePrimaryDevice = monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName;
            AppLogger.Log($"Auto-swap session baseline: {_sessionBaselinePrimaryDevice ?? "unknown"}.");
        }

        var previousWinner = _winnerSnapshot;
        _winnerSnapshot = winner.Profile.Clone();
        _winnerDetectedLauncherChild = winner.DetectedLauncherChild;

        if (!target.IsPrimary)
        {
            _displayManager.SetPrimaryByDeviceName(target.DeviceName);
            var displayName = MonitorDisplayHelper.GetDisplayName(target, _settings.Current);
            RecordProfileTriggered(winner.Profile);
            AppLogger.Log(
                $"Profile winner [{winner.Profile.DisplayLabel}, priority {winner.Profile.Priority}]: " +
                $"primary set to '{displayName}' ({target.DeviceName}).");
            StatusMessage?.Invoke(this,
                previousWinner is null
                    ? $"Auto-swap: {displayName} is now primary."
                    : $"Auto-swap: {winner.Profile.DisplayLabel} won; {displayName} is now primary.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            AppLogger.Log(
                $"Profile winner [{winner.Profile.DisplayLabel}]: '{target.Name}' is already primary.");
        }

        _windowRelocation.BeginWatch(winner.Profile, winner.DetectedLauncherChild);
        SetCurrentActiveProfile(new ActiveProfileState
        {
            ProfileId = winner.Profile.Id,
            ProfileLabel = winner.Profile.DisplayLabel,
            TargetMonitorLabel = MonitorDisplayHelper.GetDisplayName(target, _settings.Current),
        });
    }

    private void EndAutomationSession(IReadOnlyList<MonitorInfo> monitors)
    {
        if (_winnerSnapshot is null)
        {
            SetCurrentActiveProfile(null);
            return;
        }

        var departedWinner = _winnerSnapshot;
        var baselineDevice = _sessionBaselinePrimaryDevice;

        _winnerSnapshot = null;
        _winnerDetectedLauncherChild = null;
        _sessionBaselinePrimaryDevice = null;
        SetCurrentActiveProfile(null);

        if (!departedWinner.RestoreOnExit || string.IsNullOrWhiteSpace(baselineDevice))
        {
            AppLogger.Log(
                $"Auto-swap session ended [{departedWinner.DisplayLabel}]; restore disabled or no baseline saved.");
            return;
        }

        var baseline = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, baselineDevice, StringComparison.OrdinalIgnoreCase));
        if (baseline is null)
        {
            AppLogger.Log($"Auto-swap restore skipped: baseline display '{baselineDevice}' is disconnected.");
            return;
        }

        if (!baseline.IsPrimary)
        {
            _displayManager.SetPrimaryByDeviceName(baseline.DeviceName);
            var displayName = MonitorDisplayHelper.GetDisplayName(baseline, _settings.Current);
            AppLogger.Log($"Auto-swap session ended; restored baseline primary '{displayName}'.");
            StatusMessage?.Invoke(this, $"Auto-swap: restored {displayName} as primary.");
            PrimaryChanged?.Invoke(this, EventArgs.Empty);
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

    private void TrimRecentProcessStarts_NoLock()
    {
        var cutoff = DateTime.UtcNow - ProcessStartRetention;
        _recentProcessStarts.RemoveAll(p => p.SeenUtc < cutoff);
    }

    public static IReadOnlyList<LauncherChildTracker.RunningProcess> GetRunningProcesses()
    {
        var processes = Process.GetProcesses();
        var results = new List<LauncherChildTracker.RunningProcess>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                results.Add(new LauncherChildTracker.RunningProcess(
                    (uint)process.Id,
                    LauncherCatalog.Normalize(process.ProcessName)));
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

        return results;
    }

    public static HashSet<string> GetRunningProcessNames() =>
        GetRunningProcesses()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
