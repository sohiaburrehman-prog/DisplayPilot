using System.Text.Json;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Headless CLI entry points — no tray or GUI.</summary>
public static class CliCommands
{
    private const string HelpText = """
        DisplayPilot command-line interface

        Usage:
          DisplayPilot.exe [options]

        GUI (default):
          --autostart          Start hidden in the tray (used by Windows startup)

        CLI (headless — exits without opening the tray):
          --help               Show this help
          --list-monitors      Print connected monitors as JSON
          --set-primary <n>    Set primary by 0-based index or device name (\\.\DISPLAYn)
          --export-settings <path>
                               Export settings JSON to the given file
          --import-settings <path>
                               Import settings JSON (backs up the current file first)

        Examples:
          DisplayPilot.exe --list-monitors
          DisplayPilot.exe --set-primary 1
          DisplayPilot.exe --set-primary \\.\DISPLAY2
          DisplayPilot.exe --export-settings backup.json
        """;

    /// <summary>Returns true when args were handled (caller should exit with the code).</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args is null || args.Length == 0)
        {
            return false;
        }

        if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Out.WriteLine(HelpText);
            return true;
        }

        if (args.Any(a => string.Equals(a, "--list-monitors", StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = RunListMonitors();
            return true;
        }

        var setPrimaryIndex = Array.FindIndex(args, a =>
            string.Equals(a, "--set-primary", StringComparison.OrdinalIgnoreCase));
        if (setPrimaryIndex >= 0)
        {
            var value = setPrimaryIndex + 1 < args.Length ? args[setPrimaryIndex + 1] : null;
            exitCode = RunSetPrimary(value);
            return true;
        }

        var exportIndex = Array.FindIndex(args, a =>
            string.Equals(a, "--export-settings", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0)
        {
            var path = exportIndex + 1 < args.Length ? args[exportIndex + 1] : null;
            exitCode = RunExportSettings(path);
            return true;
        }

        var importIndex = Array.FindIndex(args, a =>
            string.Equals(a, "--import-settings", StringComparison.OrdinalIgnoreCase));
        if (importIndex >= 0)
        {
            var path = importIndex + 1 < args.Length ? args[importIndex + 1] : null;
            exitCode = RunImportSettings(path);
            return true;
        }

        if (args.Length == 1 &&
            string.Equals(args[0], Program.AutostartArg, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    private static int RunListMonitors()
    {
        try
        {
            var manager = new DisplayManager();
            var monitors = manager.GetMonitors();
            var payload = monitors.Select(m => new
            {
                m.Index,
                m.DeviceName,
                m.Name,
                m.Width,
                m.Height,
                m.IsPrimary,
                m.RefreshRateHz,
                m.PositionX,
                m.PositionY,
            });

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            Console.Out.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunSetPrimary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            WriteError("--set-primary requires an index or device name.");
            return 1;
        }

        try
        {
            var manager = new DisplayManager();
            var monitors = manager.GetMonitors();
            if (monitors.Count <= 1)
            {
                WriteError("Only one monitor is connected — nothing to change.");
                return 1;
            }

            MonitorInfo target;
            if (int.TryParse(value, out var index))
            {
                if (index < 0 || index >= monitors.Count)
                {
                    WriteError($"Index {index} is out of range (0–{monitors.Count - 1}).");
                    return 1;
                }

                target = manager.SetPrimaryMonitor(index);
            }
            else
            {
                var match = monitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    WriteError($"No monitor matched '{value}'.");
                    return 1;
                }

                target = manager.SetPrimaryByDeviceName(match.DeviceName);
            }

            Console.Out.WriteLine(
                $"Primary set to {target.Name} ({target.DeviceName}) [index {target.Index}].");
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunExportSettings(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            WriteError("--export-settings requires a file path.");
            return 1;
        }

        try
        {
            var settings = new SettingsService();
            settings.Load();
            var json = settings.ExportToJson();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
            Console.Out.WriteLine($"Settings exported to {fullPath}");
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunImportSettings(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            WriteError("--import-settings requires a file path.");
            return 1;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                WriteError($"File not found: {fullPath}");
                return 1;
            }

            var json = File.ReadAllText(fullPath);
            if (!SettingsService.TryParseImport(json, out var imported, out var error) || imported is null)
            {
                WriteError(error ?? "Invalid settings file.");
                return 1;
            }

            var settings = new SettingsService();
            settings.Load();
            if (!settings.BackupCurrentSettings())
            {
                WriteError("Could not back up current settings.");
                return 1;
            }

            settings.ImportReplace(imported);
            Console.Out.WriteLine($"Settings imported from {fullPath}");
            Console.Out.WriteLine($"Backup: {SettingsService.SettingsFilePath}.bak");
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static void WriteError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        AppLogger.Log($"CLI error: {message}");
    }
}
