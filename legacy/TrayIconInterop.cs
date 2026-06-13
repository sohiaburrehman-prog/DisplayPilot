using System.Runtime.InteropServices;

namespace PrimaryDisplaySwap.Native;

/// <summary>
/// Native shell tray icon API used as a fallback when WinForms NotifyIcon fails to appear.
/// </summary>
internal static class TrayIconInterop
{
    public const int NIMAdd = 0x00000000;
    public const int NIMModify = 0x00000001;
    public const int NIMDelete = 0x00000002;

    public const int NIFMessage = 0x00000001;
    public const int NIFIcon = 0x00000002;
    public const int NIFTip = 0x00000004;
    public const int NIFState = 0x00000008;
    public const int NIFInfo = 0x00000010;
    public const int NIFGuid = 0x00000020;
    public const int NIFRealtime = 0x00000040;
    public const int NIFShowTip = 0x00000080;

    public const int WMUser = 0x0400;
    public const int TrayIconMessageId = WMUser + 100;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    public static int GetNotifyIconDataSize()
    {
        // NOTIFYICONDATAW v3 size (Windows 7+).
        return Marshal.SizeOf<NOTIFYICONDATA>();
    }
}
