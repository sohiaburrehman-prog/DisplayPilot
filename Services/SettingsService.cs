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
        Current.OpenPanelHotkey ??= new AppSettings().OpenPanelHotkey;
        Current.CyclePrimaryHotkey ??= new AppSettings().CyclePrimaryHotkey;
        Current.Profiles ??= new List<AppProfile>();
        Current.Profiles.RemoveAll(p => p is null);

        foreach (var profile in Current.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }
        }

        if (Current.ProcessWatchIntervalMs < 1000)
        {
            Current.ProcessWatchIntervalMs = 1000;
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
