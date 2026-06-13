using Microsoft.Win32;

namespace PrimaryDisplaySwap.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "PrimaryDisplaySwap";
    private const string ValueName = "DisplayPilot";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is string)
            {
                return true;
            }

            return key?.GetValue(LegacyValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Could not open startup registry key.");

        if (enabled)
        {
            var exePath = GetExecutablePathForStartup();
            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Could not find the application executable.", exePath);
            }

            // --autostart makes the boot launch start hidden in the tray
            // instead of opening the control panel.
            key.SetValue(ValueName, $"\"{exePath}\" {Program.AutostartArg}");
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePathForStartup()
    {
        var path = Environment.ProcessPath ?? string.Empty;
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var builtExe = Path.Combine(AppContext.BaseDirectory, "DisplayPilot.exe");
            if (File.Exists(builtExe))
            {
                return builtExe;
            }
        }

        return path;
    }
}
