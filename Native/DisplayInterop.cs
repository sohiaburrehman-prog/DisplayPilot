using System.Runtime.InteropServices;

namespace PrimaryDisplaySwap.Native;

/// <summary>
/// P/Invoke surface for display enumeration and primary-display changes.
/// </summary>
internal static class DisplayInterop
{
    // ---- GDI display enumeration ----

    public const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    public const uint DisplayDevicePrimaryDevice = 0x00000004;
    public const uint DisplayDeviceMirroringDriver = 0x00000008;

    public const int EnumCurrentSettings = -1;
    public const int DispChangeSuccessful = 0;

    public const uint DmPosition = 0x00000020;
    public const uint DmDisplayOrientation = 0x00000080;
    public const uint DmBitsPerPel = 0x00040000;
    public const uint DmPelsWidth = 0x00080000;
    public const uint DmPelsHeight = 0x00100000;
    public const uint DmDisplayFrequency = 0x00400000;

    public const uint CdsUpdateRegistry = 0x00000001;
    public const uint CdsTest = 0x00000002;
    public const uint CdsSetPrimary = 0x00000010;
    public const uint CdsNoReset = 0x10000000;

    // ChangeDisplaySettingsEx return codes.
    public const int DispChangeRestart = 1;
    public const int DispChangeFailed = -1;
    public const int DispChangeBadMode = -2;
    public const int DispChangeNotUpdated = -3;
    public const int DispChangeBadFlags = -4;
    public const int DispChangeBadParam = -5;
    public const int DispChangeBadDualView = -6;

    public static string DescribeDispChange(int code) => code switch
    {
        DispChangeSuccessful => "the change was applied",
        DispChangeRestart => "a restart is required to apply this mode",
        DispChangeFailed => "the display driver rejected the change",
        DispChangeBadMode => "the display mode is not supported",
        DispChangeNotUpdated => "the registry could not be updated",
        DispChangeBadFlags => "invalid flags were passed",
        DispChangeBadParam => "an invalid parameter was passed",
        DispChangeBadDualView => "the operation is unsupported in dual-view",
        _ => $"unknown error ({code})",
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        uint dwflags,
        IntPtr lParam);

    // ---- DisplayConfig ----

    public const int ErrorSuccess = 0;
    public const int QdcOnlyActivePaths = 0x00000002;
    public const uint DisplayconfigPathActive = 0x00000001;

    public const uint SdcApply = 0x00000080;
    public const uint SdcValidate = 0x00000040;
    public const uint SdcUseSuppliedDisplayConfig = 0x00000020;
    public const uint SdcAllowChanges = 0x00000400;
    public const uint SdcSaveToDatabase = 0x00000200;

    // Win+P projection topologies. Combined with SdcApply these switch the
    // active display topology exactly like the Windows "Project" flyout.
    public const uint SdcTopologyInternal = 0x00000001; // PC screen only
    public const uint SdcTopologyClone    = 0x00000002; // Duplicate
    public const uint SdcTopologyExtend   = 0x00000004; // Extend
    public const uint SdcTopologyExternal = 0x00000008; // Second screen only

    /// <summary>Overload for topology changes: pass IntPtr.Zero for the path
    /// and mode arrays and an SDC_TOPOLOGY_* flag combined with SDC_APPLY.</summary>
    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(int flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        int flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        DISPLAYCONFIG_PATH_INFO[] pathArray,
        uint numModeInfoArrayElements,
        DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    // ---- Advanced color / HDR ----

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE request);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_HDR_STATE request);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAY_DEVICE
{
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;
    public uint StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEVMODE
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmDeviceName;
    public ushort dmSpecVersion;
    public ushort dmDriverVersion;
    public ushort dmSize;
    public ushort dmDriverExtra;
    public uint dmFields;
    public int dmPositionX;
    public int dmPositionY;
    public uint dmDisplayOrientation;
    public uint dmDisplayFixedOutput;
    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmFormName;
    public ushort dmLogPixels;
    public uint dmBitsPerPel;
    public uint dmPelsWidth;
    public uint dmPelsHeight;
    public uint dmDisplayFlags;
    public uint dmDisplayFrequency;
    public uint dmICMMethod;
    public uint dmICMIntent;
    public uint dmMediaType;
    public uint dmDitherType;
    public uint dmReserved1;
    public uint dmReserved2;
    public uint dmPanningWidth;
    public uint dmPanningHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate;
    public uint scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_2DREGION
{
    public uint cx;
    public uint cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
{
    public long pixelRate;
    public DISPLAYCONFIG_RATIONAL hSyncFreq;
    public DISPLAYCONFIG_RATIONAL vSyncFreq;
    public DISPLAYCONFIG_2DREGION activeSize;
    public DISPLAYCONFIG_2DREGION totalSize;
    public uint videoStandard;
    public uint scanLineOrdering;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    public const uint TypeSource = 1;

    [FieldOffset(0)] public uint infoType;
    [FieldOffset(4)] public uint id;
    [FieldOffset(8)] public LUID adapterId;
    [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    [FieldOffset(16)] public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_SOURCE_MODE
{
    public uint width;
    public uint height;
    public uint pixelFormat;
    public POINTL position;
}

internal enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
{
    GetSourceName = 1,
    GetTargetName = 2,

    /// <summary>Advanced color info (Win10 1709+). Conflates HDR and WCG/ACM.</summary>
    GetAdvancedColorInfo = 9,

    /// <summary>Legacy HDR toggle (Win10 1709+).</summary>
    SetAdvancedColorState = 10,

    /// <summary>HDR-specific advanced color info (Win11 24H2+).</summary>
    GetAdvancedColorInfo2 = 15,

    /// <summary>HDR-specific toggle (Win11 24H2+); preferred over SetAdvancedColorState.</summary>
    SetHdrState = 16,
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}

/// <summary>Advanced color state per target (Win10 1709+). The bitfield is
/// flattened into <c>value</c>; use the named bit masks.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
{
    public const uint AdvancedColorSupported = 0x1;
    public const uint AdvancedColorEnabled = 0x2;
    public const uint WideColorEnforced = 0x4;
    public const uint AdvancedColorForceDisabled = 0x8;

    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
    public uint colorEncoding;
    public uint bitsPerColorChannel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
{
    public const uint EnableAdvancedColor = 0x1;

    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
}

/// <summary>HDR-specific advanced color state (Win11 24H2+). Distinguishes
/// HDR from WCG/auto color management, unlike the legacy struct.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
{
    public const uint AdvancedColorSupported = 0x01;
    public const uint AdvancedColorActive = 0x02;
    public const uint AdvancedColorLimitedByPolicy = 0x08;
    public const uint HighDynamicRangeSupported = 0x10;
    public const uint HighDynamicRangeUserEnabled = 0x20;
    public const uint WideColorSupported = 0x40;
    public const uint WideColorUserEnabled = 0x80;

    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
    public uint colorEncoding;
    public uint bitsPerColorChannel;
    public uint activeColorMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_SET_HDR_STATE
{
    public const uint EnableHdr = 0x1;

    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
}
