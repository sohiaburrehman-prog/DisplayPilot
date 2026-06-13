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
            Padding = new Padding(4, 6, 4, 6),
        };
        _menu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = AppInfo.AppName,
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
        _menu.Items.Clear();

        try
        {
            var monitors = _displayManager.GetMonitors();

            if (monitors.Count <= 1)
            {
                _menu.Items.Add("Only one monitor detected").Enabled = false;
            }
            else
            {
                var header = new ToolStripMenuItem("Set primary display") { Enabled = false };
                _menu.Items.Add(header);

                foreach (var monitor in monitors)
                {
                    var item = new ToolStripMenuItem(monitor.DisplayLabel)
                    {
                        Checked = monitor.IsPrimary,
                        Enabled = !monitor.IsPrimary,
                    };

                    var index = monitor.Index;
                    item.Click += (_, _) => SetPrimary(index);
                    _menu.Items.Add(item);
                }

                if (monitors.Count == 2)
                {
                    _menu.Items.Add(new ToolStripSeparator());
                    var swapItem = new ToolStripMenuItem("Swap primary 1 ↔ 2");
                    swapItem.Click += (_, _) => SetPrimary(monitors.First(m => !m.IsPrimary).Index);
                    _menu.Items.Add(swapItem);
                }
            }
        }
        catch (Exception ex)
        {
            _menu.Items.Add($"Error: {ex.Message}").Enabled = false;
        }

        _menu.Items.Add(new ToolStripSeparator());

        var showPanelItem = new ToolStripMenuItem("Show control panel");
        showPanelItem.Click += (_, _) => ShowPanelRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(showPanelItem);

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _startupService.IsEnabled,
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        _menu.Items.Add(startupItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exitItem);
    }

    private void SetPrimary(int monitorIndex)
    {
        // Run off the UI thread; menu stays responsive while drivers apply
        // the new configuration.
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
                MessageBox.Show(ex.Message, "Could not set primary display",
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
            MessageBox.Show(ex.Message, "Could not update startup setting",
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
