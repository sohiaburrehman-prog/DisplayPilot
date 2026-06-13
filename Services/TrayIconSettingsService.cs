using Microsoft.Win32;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Promotes the app tray icon to the visible notification area on Windows 11.
/// </summary>
internal static class TrayIconSettingsService
{
    private const string NotifyIconSettingsKey = @"Control Panel\NotifyIconSettings";

    public static bool TryPromoteTrayIcon(string executablePath)
    {
        try
        {
            var normalizedTarget = NormalizePath(executablePath);
            using var root = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey, writable: false);
            if (root == null)
            {
                AppLogger.Log("TrayIconSettings: registry key not found.");
                return false;
            }

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var subKey = root.OpenSubKey(subKeyName, writable: true);
                if (subKey == null)
                {
                    continue;
                }

                var entryPath = subKey.GetValue("ExecutablePath") as string;
                if (string.IsNullOrWhiteSpace(entryPath))
                {
                    continue;
                }

                if (!PathsMatch(normalizedTarget, entryPath))
                {
                    continue;
                }

                subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                AppLogger.Log($"TrayIconSettings: promoted icon for GUID={subKeyName}, path={entryPath}");
                return true;
            }

            AppLogger.Log($"TrayIconSettings: no entry found yet for {normalizedTarget}");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"TrayIconSettings: promotion failed: {ex.Message}");
            return false;
        }
    }

    public static void SchedulePromotionRetries(string executablePath, int attempts = 6, int delayMs = 500)
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (TryPromoteTrayIcon(executablePath))
                {
                    return;
                }
            }

            AppLogger.Log(
                "TrayIconSettings: promotion not applied automatically. " +
                "Enable manually in Settings → Personalization → Taskbar → Other system tray icons.");
        });
    }

    private static bool PathsMatch(string left, string right)
    {
        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }
}
