using System.Reflection;

namespace PrimaryDisplaySwap;

internal static class AppInfo
{
    public const string AppName = "DisplayPilot";
    public const string AuthorName = "Sohiab Rehman";
    public const string SupportEmail = "sohiab.rehman@pm.me";

    public static string SupportMailtoUri =>
        $"mailto:{SupportEmail}?subject={Uri.EscapeDataString($"{AppName} help")}";

    public static string BuildHelpText()
    {
        return $"""
            {AppName} v{AppVersion}

            Quick tips
            • Double-click the tray icon or press Ctrl+Shift+M to open the control panel
            • Click a monitor card or use the tray menu to set primary
            • With two monitors, use Swap Displays for a one-click switch
            • Open Settings to rebind hotkeys or add an optional cycle-primary shortcut
            • Pick resolution and refresh per monitor from the panel dropdowns
            • Add auto-swap profiles in Settings — when a chosen app starts, a chosen monitor becomes primary
            • View or copy the activity log from the panel or tray menu
            • Optional update check uses the GitHub releases page only (no analytics); disable in Settings
            • Enable Start with Windows if you want DisplayPilot ready after sign-in

            Troubleshooting
            • Log file: %LOCALAPPDATA%\\DisplayPilot\\log.txt
            • Settings: %LOCALAPPDATA%\\DisplayPilot\\settings.json
            • If the tray icon is hidden, open the ^ overflow area in the taskbar
            • Some games need a restart after changing the primary display
            • If a hotkey does not work, another app may already use it — try a different combination in Settings

            Need help?
            Email {SupportEmail} with your Windows version and a short description of the issue.
            """;
    }

    public static string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
