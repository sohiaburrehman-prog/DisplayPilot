using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

using Microsoft.Win32;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

namespace PrimaryDisplaySwap;

/// <summary>
/// Application lifetime: owns the tray icon, global hotkeys, the display-change
/// listener, the auto-swap process watcher, the update check, and the panel
/// window. When launched with --autostart (the registry Run entry) it starts
/// silently in the tray.
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly bool _launchedAtStartup;
    private readonly EventWaitHandle _showPanelEvent;
    private readonly DisplayManager _displayManager = new();
    private readonly StartupService _startupService = new();
    private readonly SettingsService _settings = new();
    private readonly HotkeyService _hotkeys = new();

    private RegisteredWaitHandle? _showPanelWait;
    private TrayService? _tray;
    private PanelWindow? _panel;
    private SettingsWindow? _settingsWindow;
    private ProfilesWindow? _profilesWindow;
    private LogViewerWindow? _logWindow;
    private ProcessWatcherService? _processWatcher;
    private HwndSource? _hwndSource;

    public App(bool launchedAtStartup, EventWaitHandle showPanelEvent)
    {
        _launchedAtStartup = launchedAtStartup;
        _showPanelEvent = showPanelEvent;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _settings.Load();

        // Theme the shared brushes before any window is built so the first
        // paint is already correct. Follows the Windows accent + light/dark.
        ThemeManager.Initialize(this, _settings.Current.Theme);

        _panel = new PanelWindow(_displayManager, _startupService, _settings);
        _panel.SettingsRequested += (_, _) => ShowSettings();
        _panel.ProfilesRequested += (_, _) => ShowProfiles(beginAdd: _settings.Current.Profiles.Count == 0);
        _panel.ViewLogRequested += (_, _) => ShowLog();

        // Create the window handle without showing the window, so the global
        // hotkey works even while the panel has never been opened.
        var helper = new WindowInteropHelper(_panel);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        _hotkeys.Initialize(helper.Handle);
        ApplyHotkeys(announce: false);

        _processWatcher = new ProcessWatcherService(_settings, _displayManager);

        _tray = new TrayService(_displayManager, _startupService, _settings, _processWatcher);
        _tray.ShowPanelRequested += (_, _) => ShowPanel();
        _tray.ExitRequested += (_, _) => ExitApplication();
        _tray.PrimaryChanged += (_, _) => _panel?.RefreshMonitors();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ProfilesRequested += (_, _) => ShowProfiles(beginAdd: _settings.Current.Profiles.Count == 0);
        _tray.ViewLogRequested += (_, _) => ShowLog();
        _tray.CyclePrimaryRequested += (_, _) => CyclePrimary();
        _tray.ProfileApplied += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _panel?.RefreshMonitors();
            _tray?.RefreshMenu();
        });
        _tray.Install();

        _processWatcher.PrimaryChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _panel?.RefreshMonitors();
            _tray?.RefreshMenu();
        });
        _processWatcher.ActiveProfileChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _tray?.RefreshMenu();
            _profilesWindow?.RefreshMonitors();
        });
        _processWatcher.StatusMessage += (_, message) => Dispatcher.BeginInvoke(() =>
        {
            _panel?.ShowExternalStatus(message, success: true);
            _tray?.ShowBriefMessage(message);
        });
        _processWatcher.Start();

        _settings.Changed += OnSettingsChanged;

        // Re-read monitors when displays are added/removed/rearranged.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // A second app launch signals this event instead of starting again.
        _showPanelWait = ThreadPool.RegisterWaitForSingleObject(
            _showPanelEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowPanel),
            null,
            Timeout.Infinite,
            false);

        TrayIconSettingsService.SchedulePromotionRetries(Environment.ProcessPath ?? string.Empty);

        AppLogger.Log($"Initialized. PID={Environment.ProcessId}, autostart={_launchedAtStartup}");

        _ = RunUpdateCheckAsync();
        MaybeShowWhatsNew();

        if (!_settings.Current.FirstRunCompleted)
        {
            ShowWizard(isFirstRun: true);
            return;
        }

        if (!_launchedAtStartup)
        {
            ShowPanel();
        }

        MaybeShowTrayHint();
    }

    /// <summary>Once, after the first run, points the user at the tray icon so
    /// they know where the app went when the panel is closed.</summary>
    private void MaybeShowTrayHint()
    {
        if (!_settings.Current.FirstRunCompleted || _settings.Current.FirstRunTrayHintShown)
        {
            return;
        }

        var hotkey = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        var reopen = string.IsNullOrWhiteSpace(hotkey)
            ? "Click this icon anytime to open it."
            : $"Click this icon or press {hotkey} to open it anytime.";

        // Defer so the notification-area icon is settled before the balloon.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                _tray?.ShowTrayHint($"DisplayPilot keeps running in the tray. {reopen}");
                _settings.Update(s => s.FirstRunTrayHintShown = true);
            }),
            DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Shows the setup wizard (first run or re-run from Settings).</summary>
    public void ShowWizard(bool isFirstRun = false)
    {
        var wizard = new WizardWindow(_displayManager, _settings, _startupService, isRerun: !isFirstRun);
        var completed = wizard.ShowDialog() == true;

        if (isFirstRun && !completed)
        {
            _settings.Update(s =>
            {
                s.FirstRunCompleted = true;
                s.LastSeenVersion = AppInfo.AppVersion;
            });
        }

        if (wizard.SelectedFinishAction == WizardWindow.FinishAction.OpenPanel)
        {
            ShowPanel();
        }

        MaybeShowTrayHint();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ThemeManager.Apply(_settings.Current.Theme);
            ApplyHotkeys(announce: true);
            _processWatcher?.Reconfigure();
            _panel?.RefreshHotkeyHints();
            _panel?.RefreshProfilesSummary();
            _panel?.RefreshMonitors();
            _tray?.RefreshMenu();
            _settingsWindow?.LoadFromSettings();
            _profilesWindow?.RefreshMonitors();
        });
    }

    private void ApplyHotkeys(bool announce)
    {
        var result = _hotkeys.Apply(_settings.Current);
        if (announce && result.HasFailure)
        {
            _panel?.ShowExternalStatus(result.FailureMessage, success: false);
            AppLogger.Log(result.FailureMessage);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        AppLogger.Log("Display settings changed; refreshing.");
        Dispatcher.BeginInvoke(() =>
        {
            _panel?.RefreshMonitors();
            _tray?.RefreshMenu();
            _settingsWindow?.LoadFromSettings();
            _profilesWindow?.RefreshMonitors();
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyService.WmHotKey)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyService.OpenPanelHotkeyId)
            {
                AppLogger.Log("Hotkey (open panel) received.");
                ShowPanel();
                handled = true;
            }
            else if (id == HotkeyService.CyclePrimaryHotkeyId)
            {
                AppLogger.Log("Hotkey (cycle primary) received.");
                CyclePrimary();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void CyclePrimary()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var newPrimary = _displayManager.CyclePrimary();
                AppLogger.Log($"Cycle primary: now {newPrimary.Name}.");
                Dispatcher.BeginInvoke(() =>
                {
                    _panel?.RefreshMonitors();
                    _panel?.ShowExternalStatus($"Primary set to {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)}.", success: true);
                    _tray?.RefreshMenu();
                });
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Cycle primary failed: {ex.Message}");
                Dispatcher.BeginInvoke(() => _panel?.ShowExternalStatus(ex.Message, success: false));
            }
        });
    }

    private void MaybeShowWhatsNew()
    {
        if (!_settings.Current.FirstRunCompleted)
        {
            return;
        }

        if (ChangelogService.ShouldShowWhatsNew(_settings.Current.LastSeenVersion, AppInfo.AppVersion))
        {
            _panel?.ShowWhatsNewBanner(AppInfo.AppVersion);
        }
    }

    public void OpenChangelog(string version, string? releaseTag = null)
    {
        var body = ChangelogService.GetSectionForVersion(version);
        var title = ChangelogService.BuildWhatsNewTitle(version);
        var window = new PolicyWindow(title, body, $"{AppInfo.AppName} release notes");
        window.Show();
        window.Activate();

        if (!string.IsNullOrWhiteSpace(releaseTag))
        {
            _ = FetchAndRefreshChangelogAsync(window, releaseTag, version);
        }
    }

    private async Task FetchAndRefreshChangelogAsync(PolicyWindow window, string tag, string version)
    {
        var body = await ChangelogService.FetchReleaseBodyAsync(tag).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = ChangelogService.GetSectionForVersion(version);
        }

        var text = body;
        await Dispatcher.BeginInvoke(() => window.UpdateBody(text));
    }

    private async Task RunUpdateCheckAsync()
    {
        try
        {
            if (!_settings.Current.AutoUpdateCheckEnabled)
            {
                return;
            }

            // Throttle to at most once per 24 hours.
            if ((DateTime.UtcNow - _settings.Current.LastUpdateCheckUtc) < TimeSpan.FromHours(24))
            {
                return;
            }

            var service = new UpdateService();
            var info = await service.CheckForUpdateAsync().ConfigureAwait(false);

            _settings.Update(s => s.LastUpdateCheckUtc = DateTime.UtcNow);

            if (info is null || !info.UpdateAvailable)
            {
                return;
            }

            if (string.Equals(info.LatestTag, _settings.Current.DismissedUpdateTag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Dispatcher.BeginInvoke(() =>
            {
                _panel?.ShowUpdateBanner(info);
                _tray?.NotifyUpdateAvailable(info);
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Update check error: {ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.LogException("DispatcherUnhandledException", e.Exception);

        // Keep the app alive in the tray; a single UI fault should not kill it.
        e.Handled = true;
    }

    private void ShowPanel()
    {
        if (_panel is null)
        {
            return;
        }

        _panel.RefreshMonitors();
        _panel.ShowNearTray();
    }

    private void RunSetupWizard()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Hide();
        }

        if (_profilesWindow is not null)
        {
            _profilesWindow.Hide();
        }

        ShowWizard(isFirstRun: false);

        if (_settingsWindow is not null)
        {
            _settingsWindow.Show();
            _settingsWindow.LoadFromSettings();
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, RunSetupWizard, () => ShowProfiles());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void ShowProfiles(bool beginAdd = false)
    {
        if (_profilesWindow is null)
        {
            _profilesWindow = new ProfilesWindow(_displayManager, _settings, _processWatcher);
            _profilesWindow.Closed += (_, _) => _profilesWindow = null;
            _profilesWindow.Show();
            if (beginAdd)
            {
                _profilesWindow.BeginAddProfile();
            }
        }
        else
        {
            _profilesWindow.Activate();
            if (beginAdd)
            {
                _profilesWindow.BeginAddProfile();
            }
        }
    }

    private void ShowLog()
    {
        if (_logWindow is null)
        {
            _logWindow = new LogViewerWindow();
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }
        else
        {
            _logWindow.RefreshLog();
            _logWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        AppLogger.Log("Exit requested.");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _settings.Changed -= OnSettingsChanged;
        _showPanelWait?.Unregister(null);
        _processWatcher?.Dispose();
        _hotkeys.UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
        _tray?.Dispose();
        AppLogger.Log("Exited cleanly.");
        base.OnExit(e);
    }
}
