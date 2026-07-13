using System.Text.Json;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in
/// %LOCALAPPDATA%\DisplayPilot\settings.json. Tolerates a missing or corrupt
/// file by falling back to defaults (the bad file is backed up once).
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayPilot");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _lock = new();

    public AppSettings Current { get; private set; } = new();

    /// <summary>Raised after settings change and are persisted.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a requested settings write could not be persisted.</summary>
    public event EventHandler<string>? SaveFailed;

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    Current = new AppSettings();
                    Save_NoLock(Current);
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                Current = loaded ?? new AppSettings();
                NormalizeLoaded();
                AppLogger.Log($"Settings loaded ({Current.Profiles.Count} profile(s)).");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Settings load failed ({ex.Message}); using defaults.");
                BackupCorruptFile();
                Current = new AppSettings();
            }
        }
    }

    /// <summary>Applies a mutation to the live settings and persists the result.</summary>
    public bool Update(Action<AppSettings> mutate)
    {
        if (mutate is null)
        {
            return false;
        }

        string? saveError;
        lock (_lock)
        {
            var candidate = Current.Clone();
            mutate(candidate);
            if (!Save_NoLock(candidate, out saveError))
            {
                goto Failed;
            }

            Current = candidate;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;

    Failed:
        SaveFailed?.Invoke(this, saveError ?? "Settings could not be saved.");
        return false;
    }

    /// <summary>Persists the current settings without raising <see cref="Changed"/>.</summary>
    public bool Save()
    {
        string? saveError;
        lock (_lock)
        {
            if (Save_NoLock(Current, out saveError))
            {
                return true;
            }
        }

        SaveFailed?.Invoke(this, saveError ?? "Settings could not be saved.");
        return false;
    }

    private static bool Save_NoLock(AppSettings snapshot) => Save_NoLock(snapshot, out _);

    private static bool Save_NoLock(AppSettings snapshot, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLogger.Log($"Settings save failed: {ex.Message}");
            return false;
        }
    }

    private void NormalizeLoaded()
    {
        var upgraded = Current.SchemaVersion < AppSettings.CurrentSchemaVersion;
        NormalizeSettings(Current, migrateLegacyInstall: true);

        var restored = false;
        if (Current.Profiles.Count == 0)
        {
            TryRestoreProfilesFromBackup();
            restored = Current.Profiles.Count > 0;
        }

        if (upgraded || restored)
        {
            Save_NoLock(Current);
            if (upgraded)
            {
                AppLogger.Log($"Settings migrated to schema v{AppSettings.CurrentSchemaVersion}.");
            }
        }
    }

    private static void NormalizeSettings(AppSettings settings, bool migrateLegacyInstall)
    {
        var upgraded = settings.SchemaVersion < AppSettings.CurrentSchemaVersion;

        settings.OpenPanelHotkey ??= new AppSettings().OpenPanelHotkey;
        settings.CyclePrimaryHotkey ??= new AppSettings().CyclePrimaryHotkey;
        settings.Profiles ??= new List<AppProfile>();
        settings.Profiles.RemoveAll(p => p is null);
        settings.LayoutPresets ??= new List<LayoutPreset>();
        settings.LayoutPresets.RemoveAll(p => p is null);
        settings.MonitorNicknames ??= new Dictionary<string, string>();
        settings.LastUsedProfileId ??= string.Empty;

        if (!Enum.IsDefined(settings.ProfileConflictRule))
        {
            settings.ProfileConflictRule = ProfileConflictRule.HighestPriority;
        }

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !profileIds.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
                profileIds.Add(profile.Id);
            }

            profile.ResolvedTargetProcessName ??= string.Empty;
            profile.ExecutablePath ??= string.Empty;
            profile.WindowTitleContains ??= string.Empty;
            profile.DisplaySceneId ??= string.Empty;
            profile.MatchLauncherChildren = profile.MatchLauncherChildren || LauncherCatalog.IsKnownLauncher(profile.ProcessName);
            profile.Priority = Math.Clamp(profile.Priority, -1000, 1000);
        }

        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in settings.LayoutPresets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || !presetIds.Add(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
                presetIds.Add(preset.Id);
            }

            preset.MonitorModes ??= new Dictionary<string, DisplayModePreset>(StringComparer.OrdinalIgnoreCase);
            preset.MonitorModes = preset.MonitorModes
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            preset.MonitorStates ??= new Dictionary<string, DisplaySceneMonitorState>(StringComparer.OrdinalIgnoreCase);
            preset.MonitorStates = preset.MonitorStates
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        settings.LastSeenVersion ??= string.Empty;

        if (settings.ProcessWatchIntervalMs < 1000)
        {
            settings.ProcessWatchIntervalMs = 1000;
        }

        if (upgraded)
        {
            if (migrateLegacyInstall)
            {
                // Existing installs skip the first-run wizard.
                settings.FirstRunCompleted = true;
            }

            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        }
    }

    /// <summary>Serializes current settings for export (includes schema version).</summary>
    public string ExportToJson()
    {
        lock (_lock)
        {
            var snapshot = Current.Clone();
            snapshot.SchemaVersion = AppSettings.CurrentSchemaVersion;
            return JsonSerializer.Serialize(snapshot, JsonOptions);
        }
    }

    /// <summary>Parses and normalizes settings JSON without applying it.</summary>
    public static bool TryParseImport(string json, out AppSettings? settings, out string? error)
    {
        settings = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The file is empty.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (parsed is null)
            {
                error = "The file does not contain valid settings.";
                return false;
            }

            NormalizeSettings(parsed, migrateLegacyInstall: false);
            settings = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Backs up the live settings file to settings.json.bak.</summary>
    public bool BackupCurrentSettings()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return true;
                }

                var backup = SettingsPath + ".bak";
                File.Copy(SettingsPath, backup, overwrite: true);
                AppLogger.Log($"Settings backed up to {backup}.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Settings backup failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Replaces live settings with imported data (caller should back up first).</summary>
    public bool ImportReplace(AppSettings imported)
    {
        if (imported is null)
        {
            return false;
        }

        lock (_lock)
        {
            var candidate = imported.Clone();
            NormalizeSettings(candidate, migrateLegacyInstall: false);
            if (!Save_NoLock(candidate, out var saveError))
            {
                SaveFailed?.Invoke(this, saveError ?? "Imported settings could not be saved.");
                return false;
            }

            Current = candidate;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        AppLogger.Log($"Settings imported ({Current.Profiles.Count} profile(s)).");
        return true;
    }

    /// <summary>
    /// When the live settings file has no profiles, merge profiles from
    /// settings.json.bak (created on import or manual backup).
    /// </summary>
    private void TryRestoreProfilesFromBackup()
    {
        var backupPath = SettingsPath + ".bak";
        if (!File.Exists(backupPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(backupPath);
            var backup = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (backup?.Profiles is not { Count: > 0 })
            {
                return;
            }

            Current.Profiles = backup.Profiles
                .Where(p => p is not null)
                .Select(p => p.Clone())
                .ToList();
            NormalizeSettings(Current, migrateLegacyInstall: false);
            AppLogger.Log($"Restored {Current.Profiles.Count} profile(s) from settings.json.bak.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Profile restore from backup failed: {ex.Message}");
        }
    }

    private static void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var backup = SettingsPath + ".corrupt";
                File.Copy(SettingsPath, backup, overwrite: true);
                AppLogger.Log($"Backed up corrupt settings to {backup}.");
            }
        }
        catch
        {
            // Best effort.
        }
    }

    public static string SettingsFilePath => SettingsPath;
    public static string SettingsFolder => SettingsDirectory;
}
