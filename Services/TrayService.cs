using System.Windows;
using System.Windows.Forms;

using PrimaryDisplaySwap.Controls;

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
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _trayIcon;

    public event EventHandler? ShowPanelRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? PrimaryChanged;

    public TrayService(DisplayManager displayManager, StartupService startupService)
    {
        _displayManager = displayManager;
        _startupService = startupService;
        _trayIcon = AppIconHelper.LoadTrayIcon();

        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = AppTheme.BodyFont,
            Padding = new Padding(6, 8, 6, 8),
            AutoSize = true,
        };
        _menu.Opening += (_, _) =>
        {
            TrayUiState.TrayMenuOpen = true;
            RefreshMenu();
        };
        _menu.Closed += (_, _) => TrayUiState.TrayMenuOpen = false;

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = $"{AppInfo.AppName} — right-click for menu",
            ContextMenuStrip = _menu,
            Visible = false,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowPanelRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Install()
    {
        RefreshMenu();
        _notifyIcon.Visible = true;
    }

    public void RefreshMenu()
    {
        DisposeMenuItems(_menu.Items);

        _menu.Items.Add(CreateLabel(AppInfo.AppName, TrayMenuTags.Title));
        _menu.Items.Add(CreateLabel($"Version {AppInfo.AppVersion} · Ctrl+Shift+M", TrayMenuTags.Subtitle));
        _menu.Items.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Open control panel");
        openItem.Click += (_, _) => ShowPanelRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(openItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateLabel("DISPLAYS", TrayMenuTags.Section));

        try
        {
            var monitors = _displayManager.GetMonitors();

            if (monitors.Count <= 1)
            {
                _menu.Items.Add(CreateDisabled("Only one monitor connected"));
            }
            else
            {
                foreach (var monitor in monitors)
                {
                    var prefix = monitor.IsPrimary ? "✓ " : "   ";
                    var item = new ToolStripMenuItem($"{prefix}{monitor.DisplayLabel}")
                    {
                        Enabled = !monitor.IsPrimary,
                    };

                    var index = monitor.Index;
                    item.Click += (_, _) => SetPrimary(index);
                    _menu.Items.Add(item);
                }

                if (monitors.Count == 2)
                {
                    var swapItem = new ToolStripMenuItem("Swap primary displays");
                    swapItem.Click += (_, _) => SetPrimary(monitors.First(m => !m.IsPrimary).Index);
                    _menu.Items.Add(swapItem);
                }
            }
        }
        catch (Exception ex)
        {
            _menu.Items.Add(CreateDisabled($"Error: {ex.Message}"));
        }

        _menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _startupService.IsEnabled,
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        _menu.Items.Add(startupItem);

        _menu.Items.Add(new ToolStripSeparator());

        var legalMenu = new ToolStripMenuItem("Legal && policies");
        legalMenu.DropDown = BuildLegalSubmenu();
        _menu.Items.Add(legalMenu);

        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit DisplayPilot");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exitItem);
    }

    private static ContextMenuStrip BuildLegalSubmenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            Font = AppTheme.BodyFont,
            Padding = new Padding(4, 6, 4, 6),
        };

        var eulaItem = new ToolStripMenuItem("End User License Agreement");
        eulaItem.Click += (_, _) => ShowPolicySafely(LegalDocuments.EulaTitle, LegalDocuments.LoadEula);
        menu.Items.Add(eulaItem);

        var privacyItem = new ToolStripMenuItem("Privacy Policy");
        privacyItem.Click += (_, _) => ShowPolicySafely(LegalDocuments.PrivacyTitle, LegalDocuments.LoadPrivacyPolicy);
        menu.Items.Add(privacyItem);

        menu.Items.Add(new ToolStripSeparator());

        var helpItem = new ToolStripMenuItem("Help && support");
        helpItem.Click += (_, _) => ShowPolicySafely("Help & support", () => AppInfo.BuildHelpText());
        menu.Items.Add(helpItem);

        menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About DisplayPilot");
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
            Padding = tag switch
            {
                TrayMenuTags.Title => new Padding(8, 6, 8, 2),
                TrayMenuTags.Subtitle => new Padding(8, 0, 8, 4),
                TrayMenuTags.Section => new Padding(8, 4, 8, 2),
                _ => new Padding(8, 4, 8, 4),
            },
        };
    }

    private static ToolStripMenuItem CreateDisabled(string text)
    {
        return new ToolStripMenuItem(text) { Enabled = false };
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

    private void SetPrimary(int monitorIndex)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var newPrimary = _displayManager.SetPrimaryMonitor(monitorIndex);
                AppLogger.Log($"Tray: primary set to {newPrimary.Name}.");
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
