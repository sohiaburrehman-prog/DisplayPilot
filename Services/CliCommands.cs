using System.Text.Json;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Headless CLI entry points — no tray or GUI.</summary>
public static class CliCommands
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private static bool _jsonOutput;
    private static bool _dryRun;
    private static int _temporarySeconds;
    private static string _currentCommand = string.Empty;

    private const string HelpText = """
        DisplayPilot command-line interface

        Usage:
          DisplayPilot.exe [--json] <command> [value]

        GUI:
          --autostart                 Start hidden in the tray

        CLI (headless):
          --help                      Show this help
          --list-monitors             List connected monitors
          --list-profiles             List configured auto-swap profiles and IDs
          --list-scenes               List saved display scenes and IDs
          --list-presets              Legacy alias for --list-scenes
          --explain-profiles          Explain every profile match and the selected winner
          --set-primary <monitor>     Set primary by 0-based index, name, or device
          --set-hdr <monitor> on|off  Enable or disable HDR
          --set-projection <mode>     pc|duplicate|extend|second
          --apply-profile <name|id>   Apply an enabled profile
          --apply-scene <name|id>     Apply a saved display scene
          --preview-scene <name|id>   Preflight and describe a scene without changing displays
          --apply-preset <name|id>    Legacy alias for --apply-scene
          --capture-scene <name>      Capture the current full display scene
          --delete-scene <name|id>    Delete an unreferenced scene
          --rename-scene <name|id> <new-name>
                                      Rename a scene
          --export-scene <name|id> <path>
                                      Export one portable scene JSON file
          --import-scene <path>       Import and validate one scene JSON file
          --export-settings <path>    Export settings JSON
          --import-settings <path>    Import settings JSON after making a backup
          --interactive-help          Launch the interactive help guide

        Add --dry-run to apply-profile/apply-scene to preview without changing displays.
        Add --temporary <seconds> to apply-profile/apply-scene and automatically restore.
        Add --json anywhere to receive a consistent JSON success/error envelope.
        """;

    /// <summary>Returns true when args were handled (caller should exit with the code).</summary>
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args is null || args.Length == 0)
        {
            return false;
        }

        _jsonOutput = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        _dryRun = args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));
        _temporarySeconds = 0;
        var temporaryIndex = FindOption(args, "--temporary");
        if (temporaryIndex >= 0)
        {
            _currentCommand = "temporary";
            if (temporaryIndex + 1 >= args.Length ||
                !int.TryParse(args[temporaryIndex + 1], out _temporarySeconds) ||
                _temporarySeconds is < 1 or > 3600)
            {
                WriteError("--temporary requires a duration from 1 to 3600 seconds.");
                exitCode = 1;
                return true;
            }

            args = args.Where((_, index) => index != temporaryIndex && index != temporaryIndex + 1).ToArray();
        }
        args = args.Where(a =>
            !string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (args.Length == 0)
        {
            _currentCommand = "help";
            WriteError("A CLI command is required when --json is used.");
            exitCode = 1;
            return true;
        }

        if (args.Length == 1 && string.Equals(args[0], Program.AutostartArg, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasOption(args, "--help", "-h", "/?"))
        {
            _currentCommand = "help";
            if (_jsonOutput)
            {
                WriteSuccess(HelpText, new { help = HelpText });
            }
            else
            {
                Console.Out.WriteLine(HelpText);
            }

            return true;
        }

        if (HasOption(args, "--interactive-help"))
        {
            _currentCommand = "interactive-help";
            if (_jsonOutput)
            {
                WriteError("Interactive help cannot be used with --json.");
                exitCode = 1;
            }
            else
            {
                exitCode = RunInteractiveHelp();
            }

            return true;
        }

        if (HasOption(args, "--list-monitors"))
        {
            _currentCommand = "list-monitors";
            exitCode = RunListMonitors();
            return true;
        }

        if (HasOption(args, "--list-profiles"))
        {
            _currentCommand = "list-profiles";
            exitCode = RunListProfiles();
            return true;
        }

        if (HasOption(args, "--list-scenes", "--list-presets"))
        {
            _currentCommand = "list-scenes";
            exitCode = RunListPresets();
            return true;
        }

        if (HasOption(args, "--explain-profiles"))
        {
            _currentCommand = "explain-profiles";
            exitCode = RunExplainProfiles();
            return true;
        }

        if (TryGetOptionValue(args, "--set-primary", out var primaryValue))
        {
            _currentCommand = "set-primary";
            exitCode = RunSetPrimary(primaryValue);
            return true;
        }

        var hdrIndex = FindOption(args, "--set-hdr");
        if (hdrIndex >= 0)
        {
            _currentCommand = "set-hdr";
            var monitor = hdrIndex + 1 < args.Length ? args[hdrIndex + 1] : null;
            var state = hdrIndex + 2 < args.Length ? args[hdrIndex + 2] : null;
            exitCode = RunSetHdr(monitor, state);
            return true;
        }

        if (TryGetOptionValue(args, "--set-projection", out var projectionValue))
        {
            _currentCommand = "set-projection";
            exitCode = RunSetProjection(projectionValue);
            return true;
        }

        if (TryGetOptionValue(args, "--apply-profile", out var profileValue))
        {
            _currentCommand = "apply-profile";
            exitCode = RunApplyProfile(profileValue);
            return true;
        }

        if (TryGetAnyOptionValue(args, out var presetValue, "--apply-scene", "--apply-preset"))
        {
            _currentCommand = "apply-scene";
            exitCode = RunApplyPreset(presetValue, _dryRun);
            return true;
        }

        if (TryGetOptionValue(args, "--preview-scene", out var previewSceneValue))
        {
            _currentCommand = "preview-scene";
            exitCode = RunApplyPreset(previewSceneValue, preview: true);
            return true;
        }

        if (TryGetOptionValue(args, "--capture-scene", out var captureSceneName))
        {
            _currentCommand = "capture-scene";
            exitCode = RunCaptureScene(captureSceneName);
            return true;
        }

        if (TryGetOptionValue(args, "--delete-scene", out var deleteSceneValue))
        {
            _currentCommand = "delete-scene";
            exitCode = RunDeleteScene(deleteSceneValue);
            return true;
        }

        var renameIndex = FindOption(args, "--rename-scene");
        if (renameIndex >= 0)
        {
            _currentCommand = "rename-scene";
            exitCode = RunRenameScene(
                renameIndex + 1 < args.Length ? args[renameIndex + 1] : null,
                renameIndex + 2 < args.Length ? args[renameIndex + 2] : null);
            return true;
        }

        var exportSceneIndex = FindOption(args, "--export-scene");
        if (exportSceneIndex >= 0)
        {
            _currentCommand = "export-scene";
            exitCode = RunExportScene(
                exportSceneIndex + 1 < args.Length ? args[exportSceneIndex + 1] : null,
                exportSceneIndex + 2 < args.Length ? args[exportSceneIndex + 2] : null);
            return true;
        }

        if (TryGetOptionValue(args, "--import-scene", out var importScenePath))
        {
            _currentCommand = "import-scene";
            exitCode = RunImportScene(importScenePath);
            return true;
        }

        if (TryGetOptionValue(args, "--export-settings", out var exportPath))
        {
            _currentCommand = "export-settings";
            exitCode = RunExportSettings(exportPath);
            return true;
        }

        if (TryGetOptionValue(args, "--import-settings", out var importPath))
        {
            _currentCommand = "import-settings";
            exitCode = RunImportSettings(importPath);
            return true;
        }

        _currentCommand = "unknown";
        WriteError($"Unknown command or option: {string.Join(" ", args)}");
        exitCode = 1;
        return true;
    }

    internal static bool TryParseProjectionMode(string? value, out ProjectionMode mode)
    {
        mode = ProjectionMode.Extend;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "pc":
            case "pc-only":
            case "internal":
                mode = ProjectionMode.PcScreenOnly;
                return true;
            case "duplicate":
            case "clone":
                mode = ProjectionMode.Duplicate;
                return true;
            case "extend":
                mode = ProjectionMode.Extend;
                return true;
            case "second":
            case "second-only":
            case "external":
                mode = ProjectionMode.SecondScreenOnly;
                return true;
            default:
                return false;
        }
    }

    private static int RunListMonitors()
    {
        try
        {
            var manager = new DisplayManager();
            var monitors = manager.GetMonitors();
            var payload = monitors.Select(m =>
            {
                var hdr = manager.GetHdrStatus(m.DeviceName);
                return new
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
                    HdrSupported = hdr?.Supported ?? false,
                    HdrEnabled = hdr?.Enabled ?? false,
                };
            }).ToList();

            if (_jsonOutput)
            {
                WriteSuccess($"Found {payload.Count} monitor(s).", new { monitors = payload });
            }
            else
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(payload, OutputJsonOptions));
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunListProfiles()
    {
        try
        {
            var settings = LoadSettings();
            var payload = settings.Current.Profiles.Select(p =>
            {
                var scene = settings.Current.LayoutPresets.FirstOrDefault(item =>
                    string.Equals(item.Id, p.DisplaySceneId, StringComparison.Ordinal));
                return new
                {
                    p.Id,
                    Name = p.DisplayLabel,
                    p.ProcessName,
                    p.ResolvedTargetProcessName,
                    p.ExecutablePath,
                    p.WindowTitleContains,
                    p.TargetMonitorName,
                    p.DisplaySceneId,
                    SceneName = scene?.Name,
                    p.Priority,
                    p.Enabled,
                    p.RestoreOnExit,
                };
            }).ToList();

            if (_jsonOutput)
            {
                WriteSuccess($"Found {payload.Count} profile(s).", new
                {
                    conflictRule = settings.Current.ProfileConflictRule.ToString(),
                    profiles = payload,
                });
            }
            else if (payload.Count == 0)
            {
                Console.Out.WriteLine("No profiles configured.");
            }
            else
            {
                foreach (var profile in payload)
                {
                    Console.Out.WriteLine($"{profile.Id}  {profile.Name}  priority={profile.Priority}  enabled={profile.Enabled}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunListPresets()
    {
        try
        {
            var settings = LoadSettings();
            var payload = settings.Current.LayoutPresets.Select(p => new
            {
                p.Id,
                p.Name,
                p.PrimaryMonitorDeviceName,
                MonitorModeCount = p.MonitorModes.Count,
                MonitorStateCount = p.MonitorStates.Count,
                IsFullScene = p.IsFullScene,
            }).ToList();

            if (_jsonOutput)
            {
                WriteSuccess($"Found {payload.Count} scene(s).", new { scenes = payload });
            }
            else if (payload.Count == 0)
            {
                Console.Out.WriteLine("No display scenes configured.");
            }
            else
            {
                foreach (var preset in payload)
                {
                    Console.Out.WriteLine($"{preset.Id}  {preset.Name}  primary={preset.PrimaryMonitorDeviceName}  full={preset.IsFullScene}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunExplainProfiles()
    {
        try
        {
            var settings = LoadSettings();
            var manager = new DisplayManager();
            var processes = ProcessWatcherService.GetRunningProcesses(includeDetails: true);
            var snapshot = ProfileDiagnosticsService.Capture(
                settings.Current,
                manager.GetMonitors(),
                processes);
            if (_jsonOutput)
            {
                WriteSuccess("Profile diagnostics captured.", new { diagnostics = snapshot });
            }
            else
            {
                Console.Out.WriteLine(ProfileDiagnosticsService.FormatReport(snapshot));
            }
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
            WriteError("--set-primary requires an index, friendly name, or device name.");
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

            var match = ResolveMonitor(monitors, value, out var monitorError);
            if (match is null)
            {
                WriteError(monitorError!);
                return 1;
            }

            var target = manager.SetPrimaryByDeviceName(match.DeviceName);
            WriteSuccess(
                $"Primary set to {target.Name} ({target.DeviceName}) [index {target.Index}].",
                new { monitor = target });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunSetHdr(string? monitorValue, string? stateValue)
    {
        if (string.IsNullOrWhiteSpace(monitorValue) || string.IsNullOrWhiteSpace(stateValue))
        {
            WriteError("--set-hdr requires a monitor and a state (on|off).");
            return 1;
        }

        var enable = string.Equals(stateValue, "on", StringComparison.OrdinalIgnoreCase);
        if (!enable && !string.Equals(stateValue, "off", StringComparison.OrdinalIgnoreCase))
        {
            WriteError($"Invalid HDR state '{stateValue}' — use 'on' or 'off'.");
            return 1;
        }

        try
        {
            var manager = new DisplayManager();
            var target = ResolveMonitor(manager.GetMonitors(), monitorValue, out var monitorError);
            if (target is null)
            {
                WriteError(monitorError!);
                return 1;
            }

            var status = manager.GetHdrStatus(target.DeviceName);
            if (status is null || !status.Supported)
            {
                WriteError($"{target.Name} ({target.DeviceName}) does not report HDR support.");
                return 1;
            }

            if (status.Enabled != enable)
            {
                manager.SetHdrEnabled(target.DeviceName, enable);
            }

            WriteSuccess(
                $"HDR is {(enable ? "on" : "off")} for {target.Name} ({target.DeviceName}).",
                new { target.DeviceName, target.Name, enabled = enable, changed = status.Enabled != enable });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunSetProjection(string? value)
    {
        if (!TryParseProjectionMode(value, out var mode))
        {
            WriteError("--set-projection requires pc, duplicate, extend, or second.");
            return 1;
        }

        try
        {
            new DisplayManager().SetProjectionMode(mode);
            WriteSuccess($"Projection set to {mode.DisplayLabel()}.", new
            {
                mode = mode.ToString(),
                label = mode.DisplayLabel(),
            });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunApplyProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            WriteError("--apply-profile requires a profile name or ID.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            var matches = settings.Current.Profiles
                .Where(p => string.Equals(p.Id, value, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.DisplayLabel, value, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.ProcessName, value, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                WriteError(matches.Count == 0
                    ? $"No profile matched '{value}'. Use --list-profiles to see names and IDs."
                    : $"'{value}' matches multiple profiles; use the profile ID.");
                return 1;
            }

            var profile = matches[0];
            if (_temporarySeconds > 0 && string.IsNullOrWhiteSpace(profile.DisplaySceneId))
            {
                WriteError("--temporary requires a profile that applies a full display scene.");
                return 1;
            }
            if (_dryRun)
            {
                var dryRunManager = new DisplayManager();
                var processes = ProcessWatcherService.GetRunningProcesses(includeDetails: true);
                var snapshot = ProfileDiagnosticsService.Capture(
                    settings.Current,
                    dryRunManager.GetMonitors(),
                    processes);
                var diagnostic = snapshot.Profiles.First(p => p.ProfileId == profile.Id);
                WriteSuccess(
                    diagnostic.Summary,
                    new
                    {
                        dryRun = true,
                        profile = diagnostic,
                        wouldApply = diagnostic.Matched,
                    });
                return 0;
            }

            var manager = new DisplayManager();
            var result = ProfileApplyService.TryApply(
                profile, settings.Current, manager, settings);
            if (!result.Applied && !result.SkippedAlreadyPrimary)
            {
                WriteError(result.Message);
                return 1;
            }

            var restored = false;
            if (_temporarySeconds > 0)
            {
                if (result.RollbackScene is null)
                {
                    WriteError("The profile scene applied, but no rollback scene was available.");
                    return 1;
                }

                Thread.Sleep(TimeSpan.FromSeconds(_temporarySeconds));
                var restore = LayoutPresetService.TryRestore(result.RollbackScene, settings.Current, manager);
                if (!restore.Applied)
                {
                    WriteError($"Temporary profile applied, but restore failed: {restore.Message}");
                    return 1;
                }
                restored = true;
            }

            WriteSuccess(restored ? $"{result.Message} Restored after {_temporarySeconds} second(s)." : result.Message, new
            {
                profile = new { profile.Id, Name = profile.DisplayLabel, profile.Priority },
                applied = result.Applied,
                alreadyPrimary = result.SkippedAlreadyPrimary,
                target = result.TargetMonitor,
                scene = result.SceneName,
                temporarySeconds = _temporarySeconds > 0 ? _temporarySeconds : (int?)null,
                restored,
            });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunApplyPreset(string? value, bool preview)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            WriteError("--apply-scene requires a scene name or ID.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            var matches = settings.Current.LayoutPresets
                .Where(p => string.Equals(p.Id, value, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
            {
                WriteError(matches.Count == 0
                    ? $"No scene matched '{value}'. Use --list-scenes to see names and IDs."
                    : $"'{value}' matches multiple scenes; use the scene ID.");
                return 1;
            }

            var preset = matches[0];
            var manager = new DisplayManager();
            var result = preview
                ? LayoutPresetService.Preview(preset, settings.Current, manager)
                : LayoutPresetService.TryApply(preset, settings.Current, manager);
            if (!preview && !result.Applied)
            {
                WriteError(result.Message);
                return 1;
            }

            if (preview && !result.Valid)
            {
                WriteError(result.Message);
                return 1;
            }

            var restored = false;
            if (!preview && _temporarySeconds > 0)
            {
                if (result.RollbackScene is null)
                {
                    WriteError("The scene applied, but no rollback scene was available.");
                    return 1;
                }

                Thread.Sleep(TimeSpan.FromSeconds(_temporarySeconds));
                var restore = LayoutPresetService.TryRestore(result.RollbackScene, settings.Current, manager);
                if (!restore.Applied)
                {
                    WriteError($"Temporary scene applied, but restore failed: {restore.Message}");
                    return 1;
                }
                restored = true;
            }

            WriteSuccess(restored ? $"{result.Message} Restored after {_temporarySeconds} second(s)." : result.Message, new
            {
                scene = new { preset.Id, preset.Name, preset.IsFullScene },
                applied = result.Applied,
                dryRun = preview,
                changes = result.Changes,
                temporarySeconds = _temporarySeconds > 0 ? _temporarySeconds : (int?)null,
                restored,
            });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunCaptureScene(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            WriteError("--capture-scene requires a name.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            if (settings.Current.LayoutPresets.Any(scene =>
                    string.Equals(scene.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                WriteError($"A scene named '{name.Trim()}' already exists.");
                return 1;
            }

            var scene = LayoutPresetService.CaptureCurrent(name.Trim(), new DisplayManager());
            settings.Update(s => s.LayoutPresets.Add(scene));
            WriteSuccess($"Captured display scene '{scene.Name}'.", new { scene });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunDeleteScene(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            WriteError("--delete-scene requires a scene name or ID.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            var scene = ResolveScene(settings.Current, value, out var error);
            if (scene is null)
            {
                WriteError(error!);
                return 1;
            }

            var references = settings.Current.Profiles
                .Where(profile => string.Equals(profile.DisplaySceneId, scene.Id, StringComparison.Ordinal))
                .Select(profile => profile.DisplayLabel)
                .ToList();
            if (references.Count > 0)
            {
                WriteError(
                    $"Scene '{scene.Name}' is used by profile(s): {string.Join(", ", references)}. " +
                    "Change those profiles before deleting it.");
                return 1;
            }

            settings.Update(s => s.LayoutPresets.RemoveAll(item =>
                string.Equals(item.Id, scene.Id, StringComparison.Ordinal)));
            WriteSuccess($"Deleted display scene '{scene.Name}'.", new { scene.Id, scene.Name });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunRenameScene(string? value, string? newName)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(newName))
        {
            WriteError("--rename-scene requires a scene name or ID followed by the new name.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            var scene = ResolveScene(settings.Current, value, out var error);
            if (scene is null)
            {
                WriteError(error!);
                return 1;
            }

            var trimmed = newName.Trim();
            if (settings.Current.LayoutPresets.Any(item =>
                    !string.Equals(item.Id, scene.Id, StringComparison.Ordinal) &&
                    string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                WriteError($"A scene named '{trimmed}' already exists.");
                return 1;
            }

            var oldName = scene.Name;
            settings.Update(s =>
            {
                var live = s.LayoutPresets.First(item => string.Equals(item.Id, scene.Id, StringComparison.Ordinal));
                live.Name = trimmed;
            });
            WriteSuccess($"Renamed display scene '{oldName}' to '{trimmed}'.", new { scene.Id, oldName, name = trimmed });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunExportScene(string? value, string? path)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(path))
        {
            WriteError("--export-scene requires a scene name or ID followed by a file path.");
            return 1;
        }

        try
        {
            var settings = LoadSettings();
            var scene = ResolveScene(settings.Current, value, out var error);
            if (scene is null)
            {
                WriteError(error!);
                return 1;
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, JsonSerializer.Serialize(scene, OutputJsonOptions));
            WriteSuccess($"Scene '{scene.Name}' exported to {fullPath}", new { scene.Id, scene.Name, path = fullPath });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static int RunImportScene(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            WriteError("--import-scene requires a file path.");
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

            var scene = JsonSerializer.Deserialize<LayoutPreset>(File.ReadAllText(fullPath), OutputJsonOptions);
            if (!LayoutPresetService.TryNormalizeImported(scene, out var validationError) || scene is null)
            {
                WriteError(validationError ?? "Invalid scene file.");
                return 1;
            }

            var settings = LoadSettings();
            if (settings.Current.LayoutPresets.Any(item =>
                    string.Equals(item.Name, scene.Name, StringComparison.OrdinalIgnoreCase)))
            {
                WriteError($"A scene named '{scene.Name}' already exists.");
                return 1;
            }
            if (settings.Current.LayoutPresets.Any(item => string.Equals(item.Id, scene.Id, StringComparison.Ordinal)))
            {
                scene.Id = Guid.NewGuid().ToString("N");
            }

            settings.Update(s => s.LayoutPresets.Add(scene));
            WriteSuccess($"Imported display scene '{scene.Name}'.", new { scene, path = fullPath });
            return 0;
        }
        catch (JsonException ex)
        {
            WriteError($"Invalid scene JSON: {ex.Message}");
            return 1;
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
            var settings = LoadSettings();
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, settings.ExportToJson());
            WriteSuccess($"Settings exported to {fullPath}", new { path = fullPath });
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

            var settings = LoadSettings();
            if (!settings.BackupCurrentSettings())
            {
                WriteError("Could not back up current settings.");
                return 1;
            }

            if (!settings.ImportReplace(imported))
            {
                WriteError("Imported settings could not be persisted; current settings were left unchanged.");
                return 1;
            }

            var backup = SettingsService.SettingsFilePath + ".bak";
            WriteSuccess($"Settings imported from {fullPath}", new { path = fullPath, backup });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static SettingsService LoadSettings()
    {
        var settings = new SettingsService();
        settings.Load();
        return settings;
    }

    private static MonitorInfo? ResolveMonitor(
        IReadOnlyList<MonitorInfo> monitors,
        string value,
        out string? error)
    {
        error = null;
        if (int.TryParse(value, out var index))
        {
            if (index < 0 || index >= monitors.Count)
            {
                error = monitors.Count == 0
                    ? "No monitors are connected."
                    : $"Index {index} is out of range (0–{monitors.Count - 1}).";
                return null;
            }

            return monitors[index];
        }

        var matches = monitors.Where(m =>
            string.Equals(m.DeviceName, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Name, value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        error = matches.Count == 0
            ? $"No monitor matched '{value}'."
            : $"'{value}' matches multiple monitors; use the device name or index.";
        return null;
    }

    private static LayoutPreset? ResolveScene(AppSettings settings, string value, out string? error)
    {
        var matches = settings.LayoutPresets.Where(scene =>
            string.Equals(scene.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scene.Name, value, StringComparison.OrdinalIgnoreCase)).ToList();
        error = matches.Count switch
        {
            0 => $"No scene matched '{value}'. Use --list-scenes to see names and IDs.",
            > 1 => $"'{value}' matches multiple scenes; use the scene ID.",
            _ => null,
        };
        return matches.Count == 1 ? matches[0] : null;
    }

    private static int RunInteractiveHelp()
    {
        Console.WriteLine(HelpText);
        Console.WriteLine();
        Console.WriteLine("Tip: use --list-profiles or --list-scenes to discover stable IDs for scripts.");
        return 0;
    }

    private static bool HasOption(string[] args, params string[] names) =>
        args.Any(arg => names.Any(name => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)));

    private static int FindOption(string[] args, string name) =>
        Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetOptionValue(string[] args, string name, out string? value)
    {
        var index = FindOption(args, name);
        if (index < 0)
        {
            value = null;
            return false;
        }

        value = index + 1 < args.Length ? args[index + 1] : null;
        return true;
    }

    private static bool TryGetAnyOptionValue(string[] args, out string? value, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetOptionValue(args, name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void WriteSuccess(string message, object? data = null)
    {
        if (!_jsonOutput)
        {
            Console.Out.WriteLine(message);
            return;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            success = true,
            command = _currentCommand,
            message,
            data,
        }, OutputJsonOptions));
    }

    private static void WriteError(string message)
    {
        if (_jsonOutput)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                command = _currentCommand,
                error = message,
            }, OutputJsonOptions));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }

        AppLogger.Log($"CLI error [{_currentCommand}]: {message}");
    }
}
