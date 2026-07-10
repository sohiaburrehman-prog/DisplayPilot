using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Shared logic for manually or automatically applying an auto-swap profile.</summary>
public static class ProfileApplyService
{
    public sealed class ApplyResult
    {
        public bool Applied { get; init; }
        public bool SkippedAlreadyPrimary { get; init; }
        public bool SkippedMissingMonitor { get; init; }
        public MonitorInfo? TargetMonitor { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static ApplyResult TryApply(
        AppProfile profile,
        AppSettings settings,
        DisplayManager displayManager,
        SettingsService? settingsService = null,
        WindowRelocationService? windowRelocation = null)
    {
        if (profile is null || !profile.Enabled)
        {
            return new ApplyResult { Message = "Profile is disabled or missing." };
        }

        var monitors = displayManager.GetMonitors();
        var target = ProfileMatcher.ResolveTarget(profile, monitors);
        if (target is null)
        {
            var msg =
                $"Target '{profile.TargetMonitorName}' is not connected ({monitors.Count} display(s)).";
            AppLogger.Log($"Profile apply skipped [{profile.DisplayLabel}]: {msg}");
            return new ApplyResult { SkippedMissingMonitor = true, Message = msg };
        }

        if (target.IsPrimary)
        {
            var label = MonitorDisplayHelper.GetDisplayName(target, settings);
            RecordProfileUsed(profile, settingsService);
            windowRelocation?.BeginWatch(profile, detectedLauncherChild: null);
            AppLogger.Log($"Profile apply skip [{profile.DisplayLabel}]: '{label}' is already primary.");
            return new ApplyResult
            {
                SkippedAlreadyPrimary = true,
                TargetMonitor = target,
                Message = $"{label} is already primary.",
            };
        }

        displayManager.SetPrimaryByDeviceName(target.DeviceName);
        var displayName = MonitorDisplayHelper.GetDisplayName(target, settings);
        RecordProfileUsed(profile, settingsService);
        AppLogger.Log(
            $"Profile applied [{profile.DisplayLabel}]: primary set to '{displayName}' ({target.DeviceName}).");

        // If the game is already running with its window on another monitor
        // (e.g. it launched before the swap), move it onto the target.
        windowRelocation?.BeginWatch(profile, detectedLauncherChild: null);

        return new ApplyResult
        {
            Applied = true,
            TargetMonitor = target,
            Message = $"{displayName} is now primary.",
        };
    }

    private static void RecordProfileUsed(AppProfile profile, SettingsService? settingsService)
    {
        if (settingsService is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        settingsService.Update(s =>
        {
            s.LastUsedProfileId = profile.Id;
            var live = s.Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (live is not null)
            {
                live.LastTriggeredUtc = now;
            }
        });
    }
}
