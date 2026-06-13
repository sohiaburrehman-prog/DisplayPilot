using System.Runtime.InteropServices;

using PrimaryDisplaySwap.Controls;
using PrimaryDisplaySwap.Native;
using PrimaryDisplaySwap.Services;

namespace PrimaryDisplaySwap;

/// <summary>
/// Invisible message-only host: owns the tray icon, the Ctrl+Shift+M hotkey,
/// display-change notifications, and the control panel's lifetime.
/// </summary>
internal sealed class TrayHostForm : Form
{
    private const int WMHotKey = 0x0312;
    private const int WMDisplayChange = 0x007E;
    private const int HotKeyId = 9001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkM = 0x4D;
    private const int NativeTrayIconId = 1;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly DisplayManager _displayManager = new();
    private readonly StartupService _startupService = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _appIcon;
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly EventWaitHandle _showPanelEvent;

    private RegisteredWaitHandle? _showPanelWait;
    private MiniControlForm? _miniWindow;
    private bool _allowClose;
    private bool _hotKeyRegistered;
    private bool _trayInitialized;
    private bool _nativeTrayAdded;
    private bool _taskbarFallbackActive;
    private uint _taskbarCreatedMessage;

    public TrayHostForm(EventWaitHandle showPanelEvent)
    {
        _showPanelEvent = showPanelEvent;
        _appIcon = AppIconHelper.LoadAppIcon();
        _trayIcon = AppIconHelper.LoadTrayIcon();

        Text = AppTheme.AppName;
        Icon = _appIcon;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Size = new Size(1, 1);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);

        _menu.Renderer = new DarkMenuRenderer();
        _menu.Font = AppTheme.BodyFont;
        _menu.ShowImageMargin = false;
        _menu.Padding = new Padding(4, 6, 4, 6);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = AppTheme.AppName,
            ContextMenuStrip = _menu,
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMiniWindow();
        _menu.Opening += (_, _) => RefreshMenu();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _taskbarCreatedMessage = TrayIconInterop.RegisterWindowMessage("TaskbarCreated");
        AppLogger.Log($"TrayHost handle created. HWND=0x{Handle.ToInt64():X}, TaskbarCreated=0x{_taskbarCreatedMessage:X}");
    }

    protected override void SetVisibleCore(bool value)
    {
        // Keep the host permanently invisible while still creating its handle.
        if (!IsHandleCreated && !DesignMode)
        {
            CreateHandle();
        }

        if (!_trayInitialized && IsHandleCreated)
        {
            _trayInitialized = true;
            Initialize();
        }

        base.SetVisibleCore(false);
    }

    private void Initialize()
    {
        RefreshMenu();
        InstallTrayIcon();

        _hotKeyRegistered = RegisterHotKey(Handle, HotKeyId, ModControl | ModShift, VkM);

        _showPanelWait = ThreadPool.RegisterWaitForSingleObject(
            _showPanelEvent,
            (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                AppLogger.Log("Show-panel request received from another instance.");
                BeginInvoke(ShowMiniWindow);
            },
            null,
            Timeout.Infinite,
            false);

        AppLogger.Log(
            $"Initialized. PID={Environment.ProcessId}, hotkey={_hotKeyRegistered}, " +
            $"winFormsTray={_notifyIcon.Visible}, nativeTray={_nativeTrayAdded}");

        TrayIconSettingsService.SchedulePromotionRetries(Application.ExecutablePath);
        ShowMiniWindow();
    }

    private void InstallTrayIcon()
    {
        if (!IsHandleCreated)
        {
            AppLogger.Log("InstallTrayIcon skipped: handle not created.");
            return;
        }

        var iconHandle = _trayIcon.Handle;
        AppLogger.Log($"InstallTrayIcon: trayIconHandle=0x{iconHandle.ToInt64():X}, hwnd=0x{Handle.ToInt64():X}");

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WinForms NotifyIcon failed: {ex.Message}");
        }

        AppLogger.Log($"WinForms NotifyIcon.Visible={_notifyIcon.Visible}");

        var nativeProbe = ProbeNativeTrayIcon();
        AppLogger.Log($"Native Shell_NotifyIcon probe NIM_ADD={nativeProbe.Success}, Win32Error={nativeProbe.Error}");

        if (!_notifyIcon.Visible && !nativeProbe.Success)
        {
            AppLogger.Log("WinForms tray failed; activating native tray icon fallback.");
            if (TryAddNativeTrayIcon())
            {
                AppLogger.Log("Native tray icon fallback active.");
            }
        }
    }

    private (bool Success, int Error) ProbeNativeTrayIcon()
    {
        var data = CreateNativeTrayData();
        var added = TrayIconInterop.Shell_NotifyIcon(TrayIconInterop.NIMAdd, ref data);
        if (!added)
        {
            return (false, Marshal.GetLastWin32Error());
        }

        TrayIconInterop.Shell_NotifyIcon(TrayIconInterop.NIMDelete, ref data);
        return (true, 0);
    }

    private TrayIconInterop.NOTIFYICONDATA CreateNativeTrayData()
    {
        return new TrayIconInterop.NOTIFYICONDATA
        {
            cbSize = TrayIconInterop.GetNotifyIconDataSize(),
            hWnd = Handle,
            uID = NativeTrayIconId,
            uFlags = TrayIconInterop.NIFMessage | TrayIconInterop.NIFIcon | TrayIconInterop.NIFTip | TrayIconInterop.NIFShowTip,
            uCallbackMessage = TrayIconInterop.TrayIconMessageId,
            hIcon = _trayIcon.Handle,
            szTip = AppTheme.AppName
        };
    }

    private bool TryAddNativeTrayIcon()
    {
        RemoveNativeTrayIcon();

        var data = CreateNativeTrayData();
        var added = TrayIconInterop.Shell_NotifyIcon(TrayIconInterop.NIMAdd, ref data);
        if (!added)
        {
            AppLogger.Log($"Native NIM_ADD failed. Win32Error={Marshal.GetLastWin32Error()}");
            return false;
        }

        _nativeTrayAdded = true;
        return true;
    }

    private void RemoveNativeTrayIcon()
    {
        if (!_nativeTrayAdded || !IsHandleCreated)
        {
            return;
        }

        var data = new TrayIconInterop.NOTIFYICONDATA
        {
            cbSize = TrayIconInterop.GetNotifyIconDataSize(),
            hWnd = Handle,
            uID = NativeTrayIconId
        };

        TrayIconInterop.Shell_NotifyIcon(TrayIconInterop.NIMDelete, ref data);
        _nativeTrayAdded = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WMHotKey && m.WParam.ToInt32() == HotKeyId)
        {
            AppLogger.Log("Hotkey Ctrl+Shift+M received.");
            ShowMiniWindow();
        }
        else if (m.Msg == WMDisplayChange)
        {
            AppLogger.Log("WM_DISPLAYCHANGE received; refreshing monitor list.");
            _miniWindow?.RefreshMonitors();
            RefreshMenu();
        }
        else if (_taskbarCreatedMessage != 0 && m.Msg == _taskbarCreatedMessage)
        {
            AppLogger.Log("TaskbarCreated received; re-installing tray icon.");
            InstallTrayIcon();
            TrayIconSettingsService.SchedulePromotionRetries(Application.ExecutablePath);
        }
        else if (m.Msg == TrayIconInterop.TrayIconMessageId)
        {
            var msg = m.LParam.ToInt32();
            if (msg == 0x0203) // WM_LBUTTONDBLCLK
            {
                ShowMiniWindow();
            }
            else if (msg == 0x0205) // WM_RBUTTONUP
            {
                RefreshMenu();
                _menu.Show(Cursor.Position);
            }
        }

        base.WndProc(ref m);
    }

    private void ShowMiniWindow()
    {
        DisableTaskbarFallback();

        if (_miniWindow == null || _miniWindow.IsDisposed)
        {
            _miniWindow = new MiniControlForm(_displayManager, _startupService, _appIcon);
            _miniWindow.HideToTrayRequested += (_, _) => HideMiniWindow();
            _miniWindow.FormClosed += (_, _) => _miniWindow = null;
        }

        _miniWindow.RefreshMonitors();

        if (!_miniWindow.Visible)
        {
            _miniWindow.Show();
        }

        _miniWindow.Activate();
        _miniWindow.BringToFront();
    }

    private void HideMiniWindow()
    {
        if (_miniWindow == null || _miniWindow.IsDisposed || !_miniWindow.Visible)
        {
            return;
        }

        _miniWindow.Hide();
        EnableTaskbarFallback();

        try
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                AppTheme.AppName,
                "Hidden — click the taskbar button, double-click the tray icon, or press Ctrl+Shift+M to restore.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Balloon tip failed: {ex.Message}");
        }

        RestoreHintForm.ShowBriefHint();
        InstallTrayIcon();
        TrayIconSettingsService.TryPromoteTrayIcon(Application.ExecutablePath);
    }

    private void EnableTaskbarFallback()
    {
        if (_taskbarFallbackActive)
        {
            return;
        }

        _taskbarFallbackActive = true;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Minimized;
        AppLogger.Log("Taskbar fallback enabled (minimized button visible).");
    }

    private void DisableTaskbarFallback()
    {
        if (!_taskbarFallbackActive)
        {
            return;
        }

        _taskbarFallbackActive = false;
        WindowState = FormWindowState.Normal;
        Hide();
        ShowInTaskbar = false;
        AppLogger.Log("Taskbar fallback disabled.");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_taskbarFallbackActive &&
            WindowState == FormWindowState.Normal &&
            (_miniWindow == null || _miniWindow.IsDisposed || !_miniWindow.Visible))
        {
            ShowMiniWindow();
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (_taskbarFallbackActive &&
            (_miniWindow == null || _miniWindow.IsDisposed || !_miniWindow.Visible))
        {
            ShowMiniWindow();
        }
    }

    private void RefreshMenu()
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
                var sectionHeader = new ToolStripMenuItem("Set primary display")
                {
                    Enabled = false,
                    Font = AppTheme.CaptionFont,
                    ForeColor = AppTheme.TextMuted
                };
                _menu.Items.Add(sectionHeader);

                foreach (var monitor in monitors)
                {
                    var item = new ToolStripMenuItem(monitor.DisplayLabel)
                    {
                        Checked = monitor.IsPrimary,
                        Enabled = !monitor.IsPrimary
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
        showPanelItem.Click += (_, _) => ShowMiniWindow();
        _menu.Items.Add(showPanelItem);

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _startupService.IsEnabled
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        _menu.Items.Add(startupItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exitItem);
    }

    private void SetPrimary(int monitorIndex)
    {
        try
        {
            var newPrimary = _displayManager.SetPrimaryMonitor(monitorIndex);
            _miniWindow?.RefreshMonitors();
            _miniWindow?.ShowSwapStatus($"Primary: {newPrimary.Name}", success: true);
            RefreshMenu();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"SetPrimary failed: {ex.Message}");
            _miniWindow?.ShowSwapStatus(ex.Message, success: false);
            MessageBox.Show(ex.Message, "Could not set primary display", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleStartup(ToolStripMenuItem startupItem)
    {
        try
        {
            _startupService.SetEnabled(!startupItem.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not update startup setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        startupItem.Checked = _startupService.IsEnabled;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _miniWindow?.AllowClose();
        _notifyIcon.Visible = false;
        RemoveNativeTrayIcon();
        Close();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _showPanelWait?.Unregister(null);

        if (_hotKeyRegistered && IsHandleCreated)
        {
            UnregisterHotKey(Handle, HotKeyId);
        }

        _notifyIcon.Visible = false;
        RemoveNativeTrayIcon();
        _notifyIcon.Dispose();
        _menu.Dispose();

        if (_miniWindow != null && !_miniWindow.IsDisposed)
        {
            _miniWindow.AllowClose();
            _miniWindow.Close();
        }

        _trayIcon.Dispose();
        _appIcon.Dispose();
        AppLogger.Log("Exited cleanly.");
        base.OnFormClosed(e);
    }
}
