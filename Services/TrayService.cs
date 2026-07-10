using System.Windows;
using System.Windows.Forms;

using PrimaryDisplaySwap.Controls;
using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Owns the notification-area icon and its context menu. WinForms NotifyIcon
/// handles TaskbarCreated re-registration automatically, so no native
/// fallback machinery is needed.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly DisplayManager _displayManager;
    private readonly StartupService _startupService;
    private readonly SettingsService _settings;
    private readonly ProcessWatcherService? _processWatcher;
    private readonly WindowRelocationService _windowRelocation;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _trayIcon;
    private DateTime _lastBalloonUtc = DateTime.MinValue;
    private UpdateInfo? _updateInfo;

    public event EventHandler? ShowPanelRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? PrimaryChanged;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ProfilesRequested;
    public event EventHandler? ViewLogRequested;
    public event EventHandler? CyclePrimaryRequested;
    public event EventHandler? ProfileApplied;

    public TrayService(
        DisplayManager displayManager,
        StartupService startupService,
        SettingsService settings,
        ProcessWatcherService? processWatcher = null)
    {
        _displayManager = displayManager;
        _startupService = startupService;
        _settings = settings;
        _processWatcher = processWatcher;
        _windowRelocation = new WindowRelocationService(displayManager);
        _trayIcon = AppIconHelper.LoadTrayIcon();

        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = AppTheme.MenuBodyFont,
            Padding = new Padding(6, 8, 6, 8),
            AutoSize = true,
        };
        _menu.Opening += (_, _) =>
        {
            TrayUiState.TrayMenuOpen = true;
            AppTheme.RefreshMenuFonts();
            _menu.Font = AppTheme.MenuBodyFont;
            RefreshMenu();
        };
        _menu.Closed += (_, _) => TrayUiState.TrayMenuOpen = false;
        _menu.KeyDown += OnMenuKeyDown;

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = $"{AppInfo.AppName} — right-click for menu",
            ContextMenuStrip = _menu,
            Visible = false,
        };
        // Single left-click opens the panel (like the Windows volume/network
        // flyouts); double-click still works via the second click's MouseUp.
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowPanelRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public void Install()
    {
        RefreshMenu();
        _notifyIcon.Visible = true;
    }

    public void RefreshMenu()
    {
        DisposeMenuItems(_menu.Items);

        var shortcut = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        _menu.Items.Add(CreateLabel(AppInfo.AppName, TrayMenuTags.Title));
        _menu.Items.Add(CreateLabel(
            shortcut == "None" ? $"Version {AppInfo.AppVersion}" : $"Version {AppInfo.AppVersion} · {shortcut}",
            TrayMenuTags.Subtitle));
        _menu.Items.Add(new ToolStripSeparator());

        if (_updateInfo is not null)
        {
            var updateItem = CreateActionItem($"⬇  Update available — {_updateInfo.LatestTag}");
            updateItem.Tag = TrayMenuTags.Swap;
            updateItem.Click += (_, _) => OpenUrl(_updateInfo.ReleaseUrl);
            _menu.Items.Add(updateItem);
            _menu.Items.Add(new ToolStripSeparator());
        }

        var openItem = CreateActionItem("&Open control panel");
        openItem.Click += (_, _) => ShowPanelRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(openItem);

        IReadOnlyList<MonitorInfo> monitors = Array.Empty<MonitorInfo>();
        try
        {
            monitors = _displayManager.GetMonitors();
            UpdateTrayTooltip(monitors);

            if (monitors.Count > 0)
            {
                var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                _menu.Items.Add(CreateLabel(
                    $"Primary: {MonitorDisplayHelper.GetDisplayName(primary, _settings.Current)}",
                    TrayMenuTags.Status));
            }

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateLabel("DISPLAYS", TrayMenuTags.Section));

            if (monitors.Count == 0)
            {
                _menu.Items.Add(CreateDisabled("No displays detected"));
            }
            else if (monitors.Count == 1)
            {
                var only = monitors[0];
                _menu.Items.Add(CreateDisabled($"{MonitorDisplayHelper.GetNumberedName(only, _settings.Current)}  —  {only.SpecsLabel}"));
                _menu.Items.Add(CreateDisabled("Connect another display to swap"));
            }
            else
            {
                foreach (var monitor in monitors)
                {
                    AddMonitorItem(monitor, monitors.Count > 2);
                }
            }
        }
        catch (Exception ex)
        {
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateLabel("DISPLAYS", TrayMenuTags.Section));
            _menu.Items.Add(CreateDisabled($"Error: {ex.Message}"));
        }

        AddQuickActionsSection(monitors);

        AddProfilesSection();

        _menu.Items.Add(new ToolStripSeparator());

        var settingsItem = CreateActionItem("&Settings…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(settingsItem);

        var logItem = CreateActionItem("View &activity log…");
        logItem.Click += (_, _) => ViewLogRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(logItem);

        _menu.Items.Add(new ToolStripSeparator());

        var startupItem = CreateActionItem("Start with &Windows");
        startupItem.Checked = _startupService.IsEnabled;
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        _menu.Items.Add(startupItem);

        _menu.Items.Add(new ToolStripSeparator());

        var legalMenu = CreateActionItem("Legal && policies");
        legalMenu.DropDown = BuildLegalSubmenu();
        _menu.Items.Add(legalMenu);

        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = CreateActionItem("E&xit DisplayPilot");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exitItem);
    }

    private void AddQuickActionsSection(IReadOnlyList<MonitorInfo> monitors)
    {
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateLabel("QUICK ACTIONS", TrayMenuTags.Section));

        if (monitors.Count == 0)
        {
            _menu.Items.Add(CreateDisabled("Connect a display first"));
        }
        else if (monitors.Count == 1)
        {
            _menu.Items.Add(CreateDisabled("Connect another display to swap"));
        }
        else if (monitors.Count == 2)
        {
            AddSwapAction(monitors);
        }

        if (monitors.Count >= 2)
        {
            var cyclePrimaryItem = CreateActionItem("Cycle &primary display");
            cyclePrimaryItem.Click += (_, _) => CyclePrimaryRequested?.Invoke(this, EventArgs.Empty);
            _menu.Items.Add(cyclePrimaryItem);

            var projectionItem = CreateActionItem("Screen &projection (Win+P)");
            projectionItem.DropDown = BuildProjectionSubmenu();
            _menu.Items.Add(projectionItem);
        }

        var lastProfile = GetLastUsedProfile();
        if (lastProfile is not null)
        {
            var reapplyItem = CreateActionItem($"Re-apply last profile ({lastProfile.DisplayLabel})");
            reapplyItem.Click += (_, _) => ApplyProfile(lastProfile);
            _menu.Items.Add(reapplyItem);
        }

        var enabledProfiles = _settings.Current.Profiles.Where(p => p.Enabled).ToList();
        if (enabledProfiles.Count == 0)
        {
            _menu.Items.Add(CreateDisabled("No enabled profiles"));
            return;
        }

        var applyMenu = CreateActionItem("Apply &profile");
        foreach (var profile in enabledProfiles)
        {
            var captured = profile;
            var item = CreateActionItem(captured.DisplayLabel);
            item.Click += (_, _) => ApplyProfile(captured);
            applyMenu.DropDownItems.Add(item);
        }

        _menu.Items.Add(applyMenu);
    }

    private void ApplyProfile(AppProfile profile)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var result = ProfileApplyService.TryApply(
                    profile, _settings.Current, _displayManager, _settings, _windowRelocation);
                if (result.Applied)
                {
                    ShowFeedback($"Profile applied — {result.Message}");
                    ProfileApplied?.Invoke(this, EventArgs.Empty);
                }
                else if (result.SkippedAlreadyPrimary)
                {
                    ShowFeedback(result.Message);
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show(
                        result.Message,
                        "Could not apply profile",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Tray ApplyProfile failed: {ex.Message}");
                System.Windows.Forms.MessageBox.Show(ex.Message, "Could not apply profile",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    private void AddProfilesSection()
    {
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateLabel("AUTO-SWAP", TrayMenuTags.Section));

        var profiles = _settings.Current.Profiles;
        var enabled = profiles.Count(p => p.Enabled);

        var status = profiles.Count == 0
            ? "No profiles yet"
            : enabled == profiles.Count
                ? $"{enabled} profile{(enabled == 1 ? "" : "s")} active"
                : $"{enabled} of {profiles.Count} active";

        _menu.Items.Add(CreateLabel(status, TrayMenuTags.Status));

        var label = profiles.Count == 0 ? "&Add game profile…" : "&Manage game profiles…";
        var profilesItem = CreateActionItem(label);
        profilesItem.Click += (_, _) => ProfilesRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(profilesItem);
    }

    private void AddSwapAction(IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = monitors.First(m => m.IsPrimary);
        var other = monitors.First(m => !m.IsPrimary);
        var primaryName = MonitorDisplayHelper.GetDisplayName(primary, _settings.Current);
        var otherName = MonitorDisplayHelper.GetDisplayName(other, _settings.Current);
        var swapText = $"⇄  Swap: {primary.Index + 1} ↔ {other.Index + 1}  ({primaryName} ↔ {otherName})";

        var swapItem = CreateActionItem(swapText);
        swapItem.Tag = TrayMenuTags.Swap;
        swapItem.Padding = new Padding(8, 6, 8, 6);
        swapItem.ShortcutKeyDisplayString = "S";
        swapItem.Click += (_, _) => SwapPrimary();
        _menu.Items.Add(swapItem);
    }

    private void AddMonitorItem(MonitorInfo monitor, bool showSetPrimaryHint)
    {
        var label = MonitorDisplayHelper.GetTrayMenuLine(monitor, _settings.Current);
        if (!monitor.IsPrimary && showSetPrimaryHint)
        {
            label += "  —  click to set primary";
        }

        var item = CreateActionItem(label);
        item.Enabled = !monitor.IsPrimary;

        if (!monitor.IsPrimary)
        {
            var index = monitor.Index;
            var name = monitor.Name;
            item.Click += (_, _) => SetPrimary(index, name);
        }

        _menu.Items.Add(item);
    }

    private static ContextMenuStrip BuildLegalSubmenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = AppTheme.MenuBodyFont,
            Padding = new Padding(4, 6, 4, 6),
        };

        var eulaItem = CreateActionItem("End User License Agreement");
        eulaItem.Click += (_, _) => ShowPolicySafely(LegalDocuments.EulaTitle, LegalDocuments.LoadEula);
        menu.Items.Add(eulaItem);

        var privacyItem = CreateActionItem("Privacy Policy");
        privacyItem.Click += (_, _) => ShowPolicySafely(LegalDocuments.PrivacyTitle, LegalDocuments.LoadPrivacyPolicy);
        menu.Items.Add(privacyItem);

        var noticesItem = CreateActionItem("Third-Party Notices");
        noticesItem.Click += (_, _) => ShowPolicySafely(LegalDocuments.ThirdPartyNoticesTitle, LegalDocuments.LoadThirdPartyNotices);
        menu.Items.Add(noticesItem);

        menu.Items.Add(new ToolStripSeparator());

        var helpItem = CreateActionItem("Help && support");
        helpItem.Click += (_, _) => ShowPolicySafely("Help & support", () => AppInfo.BuildHelpText());
        menu.Items.Add(helpItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = CreateActionItem("About DisplayPilot");
        aboutItem.Click += (_, _) => ShowPolicy(
            "About",
            BuildAboutText(),
            $"Made by {AppInfo.AuthorName}");
        menu.Items.Add(aboutItem);

        return menu;
    }

    private static string BuildAboutText()
    {
        return $"""
            {AppInfo.AppName} v{AppInfo.AppVersion}

            A lightweight tray utility that changes which monitor Windows uses as the primary display — useful when games or apps always launch on the main screen.

            • Open the flyout from the tray icon or press Ctrl+Shift+M
            • Click a monitor card or use the tray menu to set primary
            • With two monitors, use Swap for a one-click switch
            • Logs: %LOCALAPPDATA%\DisplayPilot\log.txt

            DisplayPilot runs locally on your PC. See Privacy Policy for details.

            Made by {AppInfo.AuthorName}
            Help: {AppInfo.SupportEmail}
            """;
    }

    private static ToolStripMenuItem CreateLabel(string text, string tag)
    {
        return new ToolStripMenuItem(text)
        {
            Enabled = false,
            Tag = tag,
            Font = AppTheme.MenuBodyFont,
            Padding = tag switch
            {
                TrayMenuTags.Title => new Padding(8, 6, 8, 2),
                TrayMenuTags.Subtitle => new Padding(8, 0, 8, 4),
                TrayMenuTags.Section => new Padding(8, 4, 8, 2),
                TrayMenuTags.Status => new Padding(8, 2, 8, 2),
                _ => new Padding(8, 4, 8, 4),
            },
        };
    }

    private static ToolStripMenuItem CreateDisabled(string text)
    {
        return new ToolStripMenuItem(text)
        {
            Enabled = false,
            Font = AppTheme.MenuBodyFont,
        };
    }

    private static ToolStripMenuItem CreateActionItem(string text)
    {
        return new ToolStripMenuItem(text) { Font = AppTheme.MenuBodyFont };
    }

    private static void ShowPolicySafely(string title, Func<string> loadBody)
    {
        try
        {
            ShowPolicy(title, loadBody());
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not open {title}: {ex.Message}");
            System.Windows.Forms.MessageBox.Show(
                ex.Message,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void DisposeMenuItems(ToolStripItemCollection items)
    {
        if (items.Count == 0)
        {
            return;
        }

        // Snapshot then clear before disposing. Disposing an item still in the
        // collection removes it and invalidates a live foreach enumerator.
        var snapshot = new ToolStripItem[items.Count];
        items.CopyTo(snapshot, 0);
        items.Clear();

        foreach (var item in snapshot)
        {
            if (item is ToolStripMenuItem { DropDown: not null } menuItem)
            {
                var dropdown = menuItem.DropDown;
                menuItem.DropDown = null;
                dropdown.Dispose();
            }

            item.Dispose();
        }
    }

    private static void ShowPolicy(string title, string body, string? subtitle = null)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        app.Dispatcher.BeginInvoke(() =>
        {
            var window = new PolicyWindow(title, body, subtitle);
            window.Show();
            window.Activate();
        });
    }

    private void SetPrimary(int monitorIndex, string monitorName)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var newPrimary = _displayManager.SetPrimaryMonitor(monitorIndex);
                AppLogger.Log($"Tray: primary set to {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)}.");
                ShowFeedback(
                    $"{MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)} is now primary.",
                    replaceExisting: true);
                PrimaryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Tray SetPrimary failed: {ex.Message}");
                System.Windows.Forms.MessageBox.Show(ex.Message, "Could not set primary display",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    private ContextMenuStrip BuildProjectionSubmenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = AppTheme.MenuBodyFont,
            Padding = new Padding(4, 6, 4, 6),
        };

        foreach (var mode in new[]
        {
            ProjectionMode.PcScreenOnly,
            ProjectionMode.Duplicate,
            ProjectionMode.Extend,
            ProjectionMode.SecondScreenOnly,
        })
        {
            var captured = mode;
            var item = CreateActionItem(mode.DisplayLabel());
            item.Click += (_, _) => SetProjection(captured);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void SetProjection(ProjectionMode mode)
    {
        _ = Task.Run(() =>
        {
            try
            {
                _displayManager.SetProjectionMode(mode);
                ShowFeedback($"Projection: {mode.DisplayLabel()}.", replaceExisting: true);
                // The topology change raises WM_DISPLAYCHANGE, which the app
                // already listens for and uses to refresh the panel and menu.
                PrimaryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Tray SetProjection failed: {ex.Message}");
                System.Windows.Forms.MessageBox.Show(ex.Message, "Could not change projection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    private void SwapPrimary()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var newPrimary = _displayManager.SwapPrimaryBetweenTwoMonitors();
                AppLogger.Log($"Tray: swapped primary to {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)}.");
                ShowFeedback(
                    $"Swapped — {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)} is now primary.",
                    replaceExisting: true);
                PrimaryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Tray SwapPrimary failed: {ex.Message}");
                System.Windows.Forms.MessageBox.Show(ex.Message, "Could not swap displays",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
    }

    private AppProfile? GetLastUsedProfile()
    {
        var id = _settings.Current.LastUsedProfileId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _settings.Current.Profiles.FirstOrDefault(p => p.Id == id && p.Enabled);
    }

    private void UpdateTrayTooltip(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            _notifyIcon.Text = $"{AppInfo.AppName} — no displays detected";
            return;
        }

        var active = _processWatcher?.CurrentActiveProfile;
        if (active is not null)
        {
            var watchText = $"Watching: {active.ProfileLabel} -> {active.TargetMonitorLabel}";
            var activeTooltip = $"{AppInfo.AppName} — {watchText}";
            _notifyIcon.Text = activeTooltip.Length <= 63 ? activeTooltip : activeTooltip[..60] + "…";
            return;
        }

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var primaryLabel = MonitorDisplayHelper.GetDisplayName(primary, _settings.Current);
        var text = monitors.Count == 1
            ? $"{AppInfo.AppName} — {primaryLabel}"
            : $"{AppInfo.AppName} — primary: {primaryLabel} ({monitors.Count} displays)";

        // NotifyIcon.Text is limited to 63 characters.
        _notifyIcon.Text = text.Length <= 63 ? text : text[..60] + "…";
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.S)
        {
            return;
        }

        try
        {
            if (_displayManager.GetMonitors().Count != 2)
            {
                return;
            }

            SwapPrimary();
            e.Handled = true;
            _menu.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Tray menu shortcut failed: {ex.Message}");
        }
    }

    public void NotifyUpdateAvailable(UpdateInfo info)
    {
        _updateInfo = info;
        RefreshMenu();
        ShowFeedback($"DisplayPilot {info.LatestTag} is available. Right-click the tray icon to view it.");
    }

    private static void OpenUrl(string url) => UrlLaunchHelper.TryOpenWebUrl(url);

    /// <summary>Shows a throttled tray balloon (e.g. profile apply feedback).</summary>
    public void ShowBriefMessage(string message) => ShowFeedback(message);

    /// <summary>One-time onboarding balloon that points the user at the tray icon.</summary>
    public void ShowTrayHint(string message)
    {
        _lastBalloonUtc = DateTime.UtcNow;
        _notifyIcon.BalloonTipTitle = $"{AppInfo.AppName} is here";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(6000);
    }

    private void ShowFeedback(string message, bool replaceExisting = false)
    {
        if (!replaceExisting && (DateTime.UtcNow - _lastBalloonUtc).TotalSeconds < 4)
        {
            return;
        }

        _lastBalloonUtc = DateTime.UtcNow;
        _notifyIcon.BalloonTipTitle = AppInfo.AppName;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private void ToggleStartup(ToolStripMenuItem startupItem)
    {
        try
        {
            _startupService.SetEnabled(!startupItem.Checked);
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(ex.Message, "Could not update startup setting",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        startupItem.Checked = _startupService.IsEnabled;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
    }
}
