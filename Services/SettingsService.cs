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

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    Current = new AppSettings();
                    Save_NoLock();
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
    public void Update(Action<AppSettings> mutate)
    {
        if (mutate is null)
        {
            return;
        }

        lock (_lock)
        {
            mutate(Current);
            Save_NoLock();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Persists the current settings without raising <see cref="Changed"/>.</summary>
    public void Save()
    {
        lock (_lock)
        {
            Save_NoLock();
        }
    }

    private void Save_NoLock()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings save failed: {ex.Message}");
        }
    }

    private void NormalizeLoaded()
    {
        var upgraded = Current.SchemaVersion < AppSettings.CurrentSchemaVersion;
        NormalizeSettings(Current, migrateLegacyInstall: true);
        if (upgraded)
        {
            Save_NoLock();
            AppLogger.Log($"Settings migrated to schema v{AppSettings.CurrentSchemaVersion}.");
        }
    }

    private static void NormalizeSettings(AppSettings settings, bool migrateLegacyInstall)
    {
        var upgraded = settings.SchemaVersion < AppSettings.CurrentSchemaVersion;

        settings.OpenPanelHotkey ??= new AppSettings().OpenPanelHotkey;
        settings.CyclePrimaryHotkey ??= new AppSettings().CyclePrimaryHotkey;
        settings.Profiles ??= new List<AppProfile>();
        settings.Profiles.RemoveAll(p => p is null);
        settings.MonitorNicknames ??= new Dictionary<string, string>();

        foreach (var profile in settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            profile.ResolvedTargetProcessName ??= string.Empty;
            profile.MatchLauncherChildren = profile.MatchLauncherChildren || LauncherCatalog.IsKnownLauncher(profile.ProcessName);
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
            Current = imported.Clone();
            NormalizeSettings(Current, migrateLegacyInstall: false);
            Save_NoLock();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        AppLogger.Log($"Settings imported ({Current.Profiles.Count} profile(s)).");
        return true;
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
