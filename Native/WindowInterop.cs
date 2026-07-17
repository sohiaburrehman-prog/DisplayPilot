using System.Runtime.InteropServices;

namespace PrimaryDisplaySwap.Native;

/// <summary>
/// Win32 window enumeration and positioning used by the post-swap window
/// relocation feature. The app is PerMonitorV2 DPI-aware, so all rects here
/// are physical pixels in the same coordinate space as DEVMODE / DisplayConfig
/// monitor bounds.
/// </summary>
internal static class WindowInterop
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly int CenterX => Left + Width / 2;
        public readonly int CenterY => Top + Height / 2;
    }

    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000L;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const uint MonitorDefaultToPrimary = 1;
    public const uint MonitorDefaultToNearest = 2;

    /// <summary>MDT_EFFECTIVE_DPI for GetDpiForMonitor.</summary>
    public const int MdtEffectiveDpi = 0;

    /// <summary>
    /// Fresh work-area rectangle in physical pixels for the monitor nearest
    /// <paramref name="point"/> (or primary if the point cannot be resolved).
    /// Always queries Win32 — unlike WPF <c>SystemParameters.WorkArea</c>, which
    /// caches and goes stale after resolution / topology changes.
    /// </summary>
    public static bool TryGetWorkAreaPixels(POINT point, out RECT workArea, out uint dpiX, out uint dpiY)
    {
        workArea = default;
        dpiX = dpiY = 96;

        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        }

        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        workArea = info.rcWork;
        if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out dpiX, out dpiY) != 0 || dpiX == 0 || dpiY == 0)
        {
            dpiX = dpiY = 96;
        }

        return true;
    }

    public const int SW_RESTORE = 9;
    public const int SW_MAXIMIZE = 3;

    // DWMWA_CLOAKED: non-zero when the window is hidden by the shell (e.g.
    // UWP suspend, virtual desktops) despite WS_VISIBLE being set.
    public const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    public static long GetWindowStyle(IntPtr hWnd) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, GWL_STYLE).ToInt64()
            : GetWindowLong32(hWnd, GWL_STYLE);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>True when DWM reports the window as cloaked (hidden by shell).</summary>
    public static bool IsCloaked(IntPtr hWnd)
    {
        try
        {
            var hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }
}
