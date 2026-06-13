using System.Windows;
using System.Windows.Interop;

using Microsoft.Win32;

using PrimaryDisplaySwap.Services;

namespace PrimaryDisplaySwap;

/// <summary>
/// Application lifetime: owns the tray icon, the Ctrl+Shift+M hotkey, the
/// display-change listener, and the panel window. When launched with
/// --autostart (the registry Run entry) it starts silently in the tray.
/// </summary>
public partial class App : System.Windows.Application
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 9001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkM = 0x4D;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly bool _launchedAtStartup;
    private readonly EventWaitHandle _showPanelEvent;
    private readonly DisplayManager _displayManager = new();
    private readonly StartupService _startupService = new();

    private RegisteredWaitHandle? _showPanelWait;
    private TrayService? _tray;
    private PanelWindow? _panel;
    private HwndSource? _hwndSource;
    private bool _hotKeyRegistered;

    public App(bool launchedAtStartup, EventWaitHandle showPanelEvent)
    {
        _launchedAtStartup = launchedAtStartup;
        _showPanelEvent = showPanelEvent;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _panel = new PanelWindow(_displayManager, _startupService);

        // Create the window handle without showing the window, so the global
        // hotkey works even while the panel has never been opened.
        var helper = new WindowInteropHelper(_panel);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
        _hotKeyRegistered = RegisterHotKey(helper.Handle, HotKeyId, ModControl | ModShift, VkM);

        _tray = new TrayService(_displayManager, _startupService);
        _tray.ShowPanelRequested += (_, _) => ShowPanel();
        _tray.ExitRequested += (_, _) => ExitApplication();
        _tray.PrimaryChanged += (_, _) => _panel?.RefreshMonitors();
        _tray.Install();

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

        AppLogger.Log($"Initialized. PID={Environment.ProcessId}, hotkey={_hotKeyRegistered}, autostart={_launchedAtStartup}");

        if (!_launchedAtStartup)
        {
            ShowPanel();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        AppLogger.Log("Display settings changed; refreshing.");
        Dispatcher.BeginInvoke(() =>
        {
            _panel?.RefreshMonitors();
            _tray?.RefreshMenu();
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            AppLogger.Log("Hotkey Ctrl+Shift+M received.");
            ShowPanel();
            handled = true;
        }

        return IntPtr.Zero;
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

    private void ExitApplication()
    {
        AppLogger.Log("Exit requested.");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _showPanelWait?.Unregister(null);

        if (_hotKeyRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotKeyId);
        }

        _hwndSource?.RemoveHook(WndProc);
        _tray?.Dispose();
        AppLogger.Log("Exited cleanly.");
        base.OnExit(e);
    }
}
