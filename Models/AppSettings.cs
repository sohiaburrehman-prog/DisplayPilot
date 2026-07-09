namespace PrimaryDisplaySwap.Models;

/// <summary>
/// A rebindable global hotkey. Modifiers/Key use the same Win32 values that
/// RegisterHotKey expects (MOD_* and a virtual-key code).
/// </summary>
public sealed class HotkeyConfig
{
    /// <summary>Win32 MOD_* flags: ALT=1, CONTROL=2, SHIFT=4, WIN=8.</summary>
    public uint Modifiers { get; set; }

    /// <summary>Virtual-key code (e.g. 0x4D for 'M'). 0 means unbound.</summary>
    public uint Key { get; set; }

    public bool Enabled { get; set; }

    public bool IsBound => Enabled && Key != 0;

    public HotkeyConfig Clone() => new() { Modifiers = Modifiers, Key = Key, Enabled = Enabled };

    public bool Matches(HotkeyConfig other) =>
        other is not null && Modifiers == other.Modifiers && Key == other.Key;
}

/// <summary>
/// A per-application auto-swap rule: when <see cref="ProcessName"/> starts,
/// make <see cref="TargetMonitorName"/> primary; optionally restore the
/// previous primary when it exits.
/// </summary>
public sealed class AppProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _processName = string.Empty;
    private string _normalizedProcessName = string.Empty;
    private string _resolvedTargetProcessName = string.Empty;
    private string _normalizedResolvedTarget = string.Empty;

    /// <summary>Process executable name, with or without ".exe" (e.g. "game.exe" or "steam.exe").</summary>
    public string ProcessName
    {
        get => _processName;
        set
        {
            _processName = value ?? string.Empty;
            _normalizedProcessName = NormalizeProcessName(_processName);
        }
    }

    /// <summary>
    /// When <see cref="ProcessName"/> is a launcher, the actual game exe to watch.
    /// Matching uses this when set; otherwise the launcher process alone triggers the profile.
    /// </summary>
    public string ResolvedTargetProcessName
    {
        get => _resolvedTargetProcessName;
        set
        {
            _resolvedTargetProcessName = value ?? string.Empty;
            _normalizedResolvedTarget = NormalizeProcessName(_resolvedTargetProcessName);
        }
    }

    /// <summary>Friendly monitor name captured when the profile was created.</summary>
    public string TargetMonitorName { get; set; } = string.Empty;

    /// <summary>GDI device name (\\.\DISPLAY1) — a fallback match when the
    /// friendly name is ambiguous or unavailable.</summary>
    public string TargetMonitorDeviceName { get; set; } = string.Empty;

    public bool RestoreOnExit { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// After the swap, move the game's main window onto the target monitor if
    /// it opened elsewhere. Fixes games that pick their display during engine
    /// init, before the primary swap lands (launch race).
    /// </summary>
    public bool MoveWindowToTarget { get; set; } = true;

    /// <summary>
    /// When <see cref="ProcessName"/> is a launcher, also match a game exe that
    /// appears after the launcher starts (when no resolved target is set).
    /// </summary>
    public bool MatchLauncherChildren { get; set; } = true;

    /// <summary>UTC time this profile last activated (auto-swap or manual apply).</summary>
    public DateTime LastTriggeredUtc { get; set; } = DateTime.MinValue;

    /// <summary>Normalized process name without extension, lower-case.</summary>
    public string NormalizedProcessName => _normalizedProcessName;

    /// <summary>Normalized resolved target without extension, lower-case.</summary>
    public string NormalizedResolvedTarget => _normalizedResolvedTarget;

    public bool HasResolvedTarget => !string.IsNullOrWhiteSpace(ResolvedTargetProcessName);

    /// <summary>Human-readable label for lists, e.g. "Steam → eldenring.exe".</summary>
    public string DisplayLabel
    {
        get
        {
            var process = FormatProcessLabel(ProcessName);
            if (!HasResolvedTarget)
            {
                return process;
            }

            return $"{process} → {FormatProcessLabel(ResolvedTargetProcessName)}";
        }
    }

    private static string FormatProcessLabel(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    private static string NormalizeProcessName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }

    public AppProfile Clone() => new()
    {
        Id = Id,
        ProcessName = ProcessName,
        ResolvedTargetProcessName = ResolvedTargetProcessName,
        TargetMonitorName = TargetMonitorName,
        TargetMonitorDeviceName = TargetMonitorDeviceName,
        RestoreOnExit = RestoreOnExit,
        Enabled = Enabled,
        MoveWindowToTarget = MoveWindowToTarget,
        MatchLauncherChildren = MatchLauncherChildren,
        LastTriggeredUtc = LastTriggeredUtc,
    };
}

/// <summary>Optional resolution/refresh for one monitor inside a layout preset.</summary>
public sealed class DisplayModePreset
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRateHz { get; set; }

    public DisplayMode ToDisplayMode() => new()
    {
        Width = Width,
        Height = Height,
        RefreshRateHz = RefreshRateHz,
    };

    public static DisplayModePreset FromDisplayMode(DisplayMode mode) => new()
    {
        Width = mode.Width,
        Height = mode.Height,
        RefreshRateHz = mode.RefreshRateHz,
    };

    public DisplayModePreset Clone() => new()
    {
        Width = Width,
        Height = Height,
        RefreshRateHz = RefreshRateHz,
    };
}

/// <summary>
/// Saved primary monitor plus optional per-monitor display modes keyed by GDI device name.
/// </summary>
public sealed class LayoutPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string PrimaryMonitorDeviceName { get; set; } = string.Empty;
    public Dictionary<string, DisplayModePreset> MonitorModes { get; set; } = new();

    public LayoutPreset Clone() => new()
    {
        Id = Id,
        Name = Name,
        PrimaryMonitorDeviceName = PrimaryMonitorDeviceName,
        MonitorModes = MonitorModes.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Clone(),
            StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>How the UI palette is chosen.</summary>
public enum ThemePreference
{
    /// <summary>Follow the Windows apps light/dark setting and accent colour.</summary>
    System = 0,
    Dark = 1,
    Light = 2,
}

/// <summary>
/// Persisted application settings. Serialized to
/// %LOCALAPPDATA%\DisplayPilot\settings.json.
/// </summary>
public sealed class AppSettings
{
    // Win32 modifier flags.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint VkM = 0x4D;

    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>False until the first-run wizard completes (or is skipped on upgrade).</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>User nicknames keyed by GDI device name (\\.\DISPLAYn).</summary>
    public Dictionary<string, string> MonitorNicknames { get; set; } = new();

    /// <summary>Opens the control panel. Defaults to Ctrl+Shift+M.</summary>
    public HotkeyConfig OpenPanelHotkey { get; set; } = new()
    {
        Modifiers = ModControl | ModShift,
        Key = VkM,
        Enabled = true,
    };

    /// <summary>Optional hotkey that cycles the primary display. Unbound by default.</summary>
    public HotkeyConfig CyclePrimaryHotkey { get; set; } = new()
    {
        Modifiers = ModControl | ModShift,
        Key = 0,
        Enabled = false,
    };

    public List<AppProfile> Profiles { get; set; } = new();

    public List<LayoutPreset> LayoutPresets { get; set; } = new();

    /// <summary>Profile id last applied manually or via auto-swap (for tray re-apply).</summary>
    public string LastUsedProfileId { get; set; } = string.Empty;

    /// <summary>How often the process watcher polls, in milliseconds.</summary>
    public int ProcessWatchIntervalMs { get; set; } = 3000;

    public bool AutoUpdateCheckEnabled { get; set; } = true;

    /// <summary>Last time the GitHub update check ran (UTC). Throttles checks.</summary>
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;

    /// <summary>Release tag the user has already been notified about / dismissed.</summary>
    public string DismissedUpdateTag { get; set; } = string.Empty;

    /// <summary>App version for which the user last dismissed the "What's new" banner.</summary>
    public string LastSeenVersion { get; set; } = string.Empty;

    /// <summary>UI theme: follow Windows (default), or force Dark / Light.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>True once the one-time "we live in the tray" hint has been shown.</summary>
    public bool FirstRunTrayHintShown { get; set; }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        FirstRunCompleted = FirstRunCompleted,
        Theme = Theme,
        FirstRunTrayHintShown = FirstRunTrayHintShown,
        MonitorNicknames = new Dictionary<string, string>(MonitorNicknames),
        OpenPanelHotkey = OpenPanelHotkey.Clone(),
        CyclePrimaryHotkey = CyclePrimaryHotkey.Clone(),
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
        LayoutPresets = LayoutPresets.Select(p => p.Clone()).ToList(),
        LastUsedProfileId = LastUsedProfileId,
        ProcessWatchIntervalMs = ProcessWatchIntervalMs,
        AutoUpdateCheckEnabled = AutoUpdateCheckEnabled,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        DismissedUpdateTag = DismissedUpdateTag,
        LastSeenVersion = LastSeenVersion,
    };
}
