using System.Diagnostics;

namespace PrimaryDisplaySwap;

/// <summary>Opens files and folders under %LOCALAPPDATA%\DisplayPilot via explorer.exe with validated paths.</summary>
internal static class LocalAppLaunchHelper
{
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayPilot");

    public static string AppDataRootPath => AppDataRoot;

    public static bool IsUnderAppDataRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('"'))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(AppDataRoot);
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryOpenLogFileInExplorer(string? logPath)
    {
        if (!IsUnderAppDataRoot(logPath))
        {
            AppLogger.Log($"Blocked opening log file outside app data: '{logPath}'");
            return false;
        }

        var safePath = Path.GetFullPath(logPath!);
        return TryStartExplorer($"/select,\"{safePath}\"");
    }

    public static bool TryOpenLogFolder(string? folderPath = null)
    {
        var path = folderPath ?? AppDataRoot;
        if (!IsUnderAppDataRoot(path))
        {
            AppLogger.Log($"Blocked opening folder outside app data: '{path}'");
            return false;
        }

        var safePath = Path.GetFullPath(path);
        return TryStartExplorer($"\"{safePath}\"");
    }

    private static bool TryStartExplorer(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not start explorer.exe: {ex.Message}");
            return false;
        }
    }
}
