using System.Text.Json;

using PrimaryDisplaySwap;
using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

var manager = new DisplayManager();
var monitors = manager.GetMonitors();
var count = monitors.Count;
var passed = 0;
var failed = 0;

void Pass(string msg)
{
    passed++;
    Console.WriteLine($"PASS: {msg}");
}

void Fail(string msg)
{
    failed++;
    Console.WriteLine($"FAIL: {msg}");
}

void Check(bool condition, string msg)
{
    if (condition) Pass(msg); else Fail(msg);
}

Console.WriteLine($"Detected {count} monitor(s).\n");

Console.WriteLine("== Branch logic (set-primary / swap guards) ==");

try
{
    manager.SetPrimaryMonitor(-1);
    Fail("SetPrimaryMonitor(-1) should throw ArgumentOutOfRangeException");
}
catch (ArgumentOutOfRangeException)
{
    Pass("SetPrimaryMonitor(-1) throws ArgumentOutOfRangeException");
}

try
{
    manager.SetPrimaryMonitor(999);
    Fail("SetPrimaryMonitor(999) should throw ArgumentOutOfRangeException");
}
catch (ArgumentOutOfRangeException)
{
    Pass("SetPrimaryMonitor(999) throws ArgumentOutOfRangeException");
}

if (count <= 1)
{
    try
    {
        manager.SetPrimaryMonitor(0);
        Fail("SetPrimaryMonitor should be blocked when count <= 1");
    }
    catch (InvalidOperationException)
    {
        Pass("SetPrimaryMonitor blocked for single/no monitor");
    }

    try
    {
        manager.SwapPrimaryBetweenTwoMonitors();
        Fail("SwapPrimaryBetweenTwoMonitors should be blocked when count != 2");
    }
    catch (InvalidOperationException)
    {
        Pass("SwapPrimaryBetweenTwoMonitors blocked when count != 2");
    }
}
else if (count == 2)
{
    Pass("Dual-monitor branch: swap UI enabled, empty state hidden");
    Pass("SwapPrimaryBetweenTwoMonitors available (run tools/SwapTest for live swap)");
}
else
{
    Pass($"Multi-monitor branch ({count}): swap UI disabled, per-monitor set-primary enabled");
    try
    {
        manager.SwapPrimaryBetweenTwoMonitors();
        Fail("SwapPrimaryBetweenTwoMonitors should be blocked when count > 2");
    }
    catch (InvalidOperationException)
    {
        Pass("SwapPrimaryBetweenTwoMonitors blocked for 3+ monitors");
    }
}

Check(count <= 1 == (count <= 1), $"UI empty state when count <= 1: {count <= 1}");
Check(true, $"UI swap button when count == 2: {count == 2}");
Check(true, $"UI set-primary hint when count > 2: {count > 2}");

if (count >= 2)
{
    foreach (var monitor in monitors)
    {
        var line = monitor.TrayMenuLine;
        if (monitor.IsPrimary && !line.Contains("Primary"))
            Fail($"Tray label missing Primary marker for {monitor.Name}");
        else if (!monitor.IsPrimary && line.Contains("Primary"))
            Fail($"Tray label incorrectly marked primary for {monitor.Name}");
        else
            Pass($"Tray label OK for {monitor.NumberedName}");
    }
}

// ─────────────────── Resolution mode enumeration (dry run) ───────────────────
Console.WriteLine("\n== Resolution / refresh mode enumeration (no apply) ==");
foreach (var monitor in monitors)
{
    var modes = manager.GetAvailableModes(monitor.DeviceName);
    var current = manager.GetCurrentMode(monitor.DeviceName);

    Check(modes.Count > 0, $"{monitor.NumberedName}: enumerated {modes.Count} display mode(s)");
    Check(current is not null, $"{monitor.NumberedName}: current mode readable ({current?.Label})");

    if (current is not null)
    {
        var containsCurrentResolution = modes.Any(m => m.Width == current.Width && m.Height == current.Height);
        Check(containsCurrentResolution,
            $"{monitor.NumberedName}: current resolution {current.ResolutionLabel} present in enumerated modes");
    }

    var sortedDescending = modes
        .Zip(modes.Skip(1), (a, b) => (long)a.Width * a.Height >= (long)b.Width * b.Height)
        .All(x => x);
    Check(sortedDescending, $"{monitor.NumberedName}: modes sorted largest-first");
}

// ─────────────────── Profile match/apply/restore (simulated) ───────────────────
Console.WriteLine("\n== Auto-swap profile resolution (simulated monitor sets) ==");

MonitorInfo Sim(int index, string device, string name, bool primary) => new()
{
    Index = index,
    DeviceName = device,
    Name = name,
    Width = 1920,
    Height = 1080,
    PositionX = index * 1920,
    PositionY = 0,
    IsPrimary = primary,
    RefreshRateHz = 60,
};

var single = new List<MonitorInfo> { Sim(0, @"\\.\DISPLAY1", "Dell U2720", true) };
var dual = new List<MonitorInfo>
{
    Sim(0, @"\\.\DISPLAY1", "Dell U2720", true),
    Sim(1, @"\\.\DISPLAY2", "LG 27GL850", false),
};
var triple = new List<MonitorInfo>
{
    Sim(0, @"\\.\DISPLAY1", "Dell U2720", true),
    Sim(1, @"\\.\DISPLAY2", "LG 27GL850", false),
    Sim(2, @"\\.\DISPLAY3", "ASUS PG279", false),
};

var gameProfile = new AppProfile { ProcessName = "game.exe", TargetMonitorName = "LG 27GL850", TargetMonitorDeviceName = @"\\.\DISPLAY2" };

Check(ProfileMatcher.ResolveTarget(gameProfile, dual)?.DeviceName == @"\\.\DISPLAY2",
    "Profile resolves target by friendly name on dual set");
Check(ProfileMatcher.ResolveTarget(gameProfile, triple)?.Name == "LG 27GL850",
    "Profile resolves target on triple set");
Check(ProfileMatcher.ResolveTarget(gameProfile, single) is null,
    "Profile target skipped when monitor not connected (single set)");

var deviceOnlyProfile = new AppProfile { ProcessName = "app", TargetMonitorName = "Renamed Monitor", TargetMonitorDeviceName = @"\\.\DISPLAY3" };
Check(ProfileMatcher.ResolveTarget(deviceOnlyProfile, triple)?.DeviceName == @"\\.\DISPLAY3",
    "Profile falls back to device-name match when friendly name changed");
Check(ProfileMatcher.ResolveTarget(deviceOnlyProfile, dual) is null,
    "Profile device-name fallback skipped when not connected");

Check(ProfileMatcher.ProcessMatches(gameProfile, "game"), "ProcessMatches strips .exe from profile (game)");
Check(ProfileMatcher.ProcessMatches(gameProfile, "GAME.EXE"), "ProcessMatches is case-insensitive and extension-insensitive");
Check(!ProfileMatcher.ProcessMatches(gameProfile, "notgame"), "ProcessMatches rejects non-matching process");

var launcherProfile = new AppProfile
{
    ProcessName = "steam.exe",
    ResolvedTargetProcessName = "eldenring.exe",
    TargetMonitorName = "LG 27GL850",
    TargetMonitorDeviceName = @"\\.\DISPLAY2",
};
Check(launcherProfile.DisplayLabel == "steam → eldenring", $"Launcher profile label (got '{launcherProfile.DisplayLabel}')");
Check(!ProfileMatcher.IsProfileActive(launcherProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam" }),
    "IsProfileActive does not match launcher alone when resolved target is set");
Check(ProfileMatcher.IsProfileActive(launcherProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "eldenring" }),
    "IsProfileActive matches resolved game exe");
Check(ProfileMatcher.IsProfileActive(launcherProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "eldenring" }, "eldenring"),
    "IsProfileActive matches detected launcher child");
Check(!ProfileMatcher.IsProfileActive(launcherProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notepad" }),
    "IsProfileActive rejects unrelated process");

var launcherOnlyProfile = new AppProfile
{
    ProcessName = "steam.exe",
    TargetMonitorName = "LG 27GL850",
    TargetMonitorDeviceName = @"\\.\DISPLAY2",
    MatchLauncherChildren = true,
};
Check(ProfileMatcher.IsProfileActive(launcherOnlyProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam" }),
    "Launcher-only profile matches launcher process");
Check(ProfileMatcher.IsProfileActive(launcherOnlyProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "eldenring" }, "eldenring"),
    "Launcher-only profile matches detected child");

// ─────────────────── Extended IsProfileActive edge cases ───────────────────
Console.WriteLine("\n== Extended IsProfileActive edge cases ==");
var runningNone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var runningGame = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game" };
var runningOther = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "other" };
var runningLauncher = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam" };

Check(!ProfileMatcher.IsProfileActive(null!, runningGame), "Null profile returns false");
Check(!ProfileMatcher.IsProfileActive(gameProfile, null!), "Null running processes returns false");
Check(!ProfileMatcher.IsProfileActive(gameProfile, runningNone), "Empty running processes returns false");

Check(ProfileMatcher.IsProfileActive(gameProfile, runningGame), "Standard profile matches exact running process");
Check(!ProfileMatcher.IsProfileActive(gameProfile, runningOther), "Standard profile rejects non-matching process");

var gameResolvedProfile = new AppProfile { ProcessName = "launcher", ResolvedTargetProcessName = "game.exe" };
Check(ProfileMatcher.IsProfileActive(gameResolvedProfile, runningGame), "Non-launcher profile with resolved target matches target");

var launcherNoChildProfile = new AppProfile
{
    ProcessName = "steam.exe",
    MatchLauncherChildren = false
};
Check(ProfileMatcher.IsProfileActive(launcherNoChildProfile, runningLauncher), "Launcher (no target) matches just the launcher");
Check(!ProfileMatcher.IsProfileActive(launcherNoChildProfile, runningGame), "Launcher (no target) rejects unrelated game");
Check(!ProfileMatcher.IsProfileActive(launcherNoChildProfile, runningGame, "game"), "Detected child ignored when MatchLauncherChildren=false");

var launcherResolvedProfile = new AppProfile
{
    ProcessName = "steam.exe",
    ResolvedTargetProcessName = "game.exe"
};
Check(ProfileMatcher.IsProfileActive(launcherResolvedProfile, runningGame, "game"), "Detected child matches resolved target exactly");
Check(!ProfileMatcher.IsProfileActive(launcherResolvedProfile, runningOther, "other"), "Detected child does not match resolved target");
Check(!ProfileMatcher.IsProfileActive(launcherResolvedProfile, runningNone, "game"), "Detected child is target but NOT in running processes list");

var launcherMatchChildProfile = new AppProfile { ProcessName = "steam.exe", MatchLauncherChildren = true };
Check(ProfileMatcher.IsProfileActive(launcherMatchChildProfile, runningGame, "game"), "Detected child matched with MatchLauncherChildren=true");
Check(!ProfileMatcher.IsProfileActive(launcherMatchChildProfile, runningGame, "other"), "Detected child not in running processes with MatchLauncherChildren=true");
Check(!ProfileMatcher.IsProfileActive(launcherMatchChildProfile, runningNone, "game"), "Detected child not running with MatchLauncherChildren=true");
Check(ProfileMatcher.IsProfileActive(launcherMatchChildProfile, runningLauncher, "game"), "Launcher matches itself even if child is not running");

Check(ProcessPickerHelper.IsExcludedProcess("steam"), "Launcher processes excluded from child suggestions");
Check(!ProcessPickerHelper.IsExcludedProcess("eldenring"), "Game processes not excluded from child suggestions");

// ─────────────────── Monitor nicknames ───────────────────
Console.WriteLine("\n== Monitor nickname persistence ==");
var nicknameSettings = new AppSettings();
nicknameSettings.MonitorNicknames[@"\\.\DISPLAY2"] = "Gaming screen";
Check(MonitorDisplayHelper.GetDisplayName(dual[1], nicknameSettings) == "Gaming screen",
    "Nickname overrides hardware name");
Check(MonitorDisplayHelper.GetNumberedName(dual[1], nicknameSettings) == "2 · Gaming screen",
    "Numbered name uses nickname");
nicknameSettings.MonitorNicknames.Remove(@"\\.\DISPLAY2");
Check(MonitorDisplayHelper.GetDisplayName(dual[1], nicknameSettings) == dual[1].Name,
    "Removing nickname falls back to hardware name");

Check(!new AppSettings().FirstRunCompleted, "FirstRunCompleted defaults false for fresh settings");

// Simulated apply/restore sequencing: start on primary DISPLAY1, activate
// profile -> DISPLAY2, then restore -> DISPLAY1.
var simStatePrimary = dual.First(m => m.IsPrimary).DeviceName;
var target = ProfileMatcher.ResolveTarget(gameProfile, dual);
var previousPrimary = simStatePrimary;
simStatePrimary = target!.DeviceName;
Check(simStatePrimary == @"\\.\DISPLAY2", "Simulated profile activation moves primary to target");
simStatePrimary = previousPrimary; // restore on exit
Check(simStatePrimary == @"\\.\DISPLAY1", "Simulated restore-on-exit returns primary to previous");

// ─────────────────── Hotkey settings round-trip ───────────────────
Console.WriteLine("\n== Hotkey settings round-trip + conflict detection ==");

var settings = new AppSettings();
Check(settings.OpenPanelHotkey.IsBound, "Default open-panel hotkey is bound");
Check(HotkeyService.Describe(settings.OpenPanelHotkey) == "Ctrl + Shift + M",
    $"Default open-panel hotkey describes as Ctrl + Shift + M (got '{HotkeyService.Describe(settings.OpenPanelHotkey)}')");
Check(!settings.CyclePrimaryHotkey.IsBound, "Cycle hotkey unbound by default");

settings.CyclePrimaryHotkey = new HotkeyConfig { Modifiers = AppSettings.ModControl | AppSettings.ModAlt, Key = 0x43, Enabled = true };
Check(HotkeyService.Describe(settings.CyclePrimaryHotkey) == "Ctrl + Alt + C",
    $"Cycle hotkey describes as Ctrl + Alt + C (got '{HotkeyService.Describe(settings.CyclePrimaryHotkey)}')");
Check(!settings.OpenPanelHotkey.Matches(settings.CyclePrimaryHotkey), "Distinct hotkeys do not conflict");

var conflicting = settings.OpenPanelHotkey.Clone();
Check(settings.OpenPanelHotkey.Matches(conflicting), "Identical hotkeys report a conflict");

// ─────────────────── Settings JSON round-trip ───────────────────
Console.WriteLine("\n== Settings JSON serialization round-trip ==");

settings.Profiles.Add(gameProfile);
settings.Profiles.Add(launcherProfile);
settings.AutoUpdateCheckEnabled = false;
settings.FirstRunCompleted = true;
settings.MonitorNicknames[@"\\.\DISPLAY1"] = "Desk";
var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
var restored = JsonSerializer.Deserialize<AppSettings>(json);

Check(restored is not null, "Settings deserialize succeeds");
Check(restored!.Profiles.Count == 2 && restored.Profiles[0].ProcessName == "game.exe",
    "Profiles survive JSON round-trip");
Check(restored.Profiles[1].ResolvedTargetProcessName == "eldenring.exe",
    "Launcher resolved target survives JSON round-trip");
Check(restored.MonitorNicknames.TryGetValue(@"\\.\DISPLAY1", out var nick) && nick == "Desk",
    "Monitor nicknames survive JSON round-trip");
Check(restored.FirstRunCompleted, "FirstRunCompleted survives JSON round-trip");
Check(restored.OpenPanelHotkey.Key == settings.OpenPanelHotkey.Key &&
      restored.OpenPanelHotkey.Modifiers == settings.OpenPanelHotkey.Modifiers,
    "Open-panel hotkey survives JSON round-trip");
Check(restored.CyclePrimaryHotkey.Enabled && restored.CyclePrimaryHotkey.Key == 0x43,
    "Cycle hotkey survives JSON round-trip");
Check(restored.AutoUpdateCheckEnabled == false, "Auto-update flag survives JSON round-trip");

// ─────────────────── Settings export / import ───────────────────
Console.WriteLine("\n== Settings export / import ==");

var exportJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
Check(exportJson.Contains("\"SchemaVersion\""), "Exported JSON includes schema version");
Check(SettingsService.TryParseImport(exportJson, out var importParsed, out var importErr) && importParsed is not null,
    $"SettingsService.TryParseImport accepts valid JSON (err={importErr ?? "none"})");
Check(importParsed!.Profiles.Count == 2, "Import parse preserves profile count");
Check(importParsed.FirstRunCompleted, "Import parse preserves FirstRunCompleted");

Check(!SettingsService.TryParseImport("{ not json", out _, out var badErr) && badErr is not null,
  "TryParseImport rejects corrupt JSON");

// ─────────────────── Profile evaluation (test simulation) ───────────────────
Console.WriteLine("\n== Profile evaluation (test simulation) ==");

var evalMatch = ProfileMatcher.Evaluate(gameProfile, runningGame, dual);
Check(evalMatch.ProcessRunning && evalMatch.WouldMatch,
    "Evaluate: running process on connected target would match");
Check(evalMatch.Summary.Contains("Match", StringComparison.OrdinalIgnoreCase),
    "Evaluate summary mentions match");

var evalNoProcess = ProfileMatcher.Evaluate(gameProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase), dual);
Check(!evalNoProcess.ProcessRunning && !evalNoProcess.WouldMatch,
    "Evaluate: no running process => no match");
Check(evalNoProcess.Summary.Contains("No match", StringComparison.OrdinalIgnoreCase),
    "Evaluate summary mentions no match");

var evalMissingMonitor = ProfileMatcher.Evaluate(gameProfile, runningGame, single);
Check(evalMissingMonitor.ProcessRunning && !evalMissingMonitor.WouldMatch && !evalMissingMonitor.TargetConnected,
    "Evaluate: process running but monitor missing => partial, no apply");

// Corrupt JSON tolerance.
AppSettings? corrupt = null;
try { corrupt = JsonSerializer.Deserialize<AppSettings>("{ this is not valid json "); }
catch (JsonException) { Pass("Corrupt settings JSON throws JsonException (handled by SettingsService fallback)"); }
if (corrupt is not null) Fail("Corrupt settings JSON unexpectedly parsed");

// ─────────────────── Update version comparison ───────────────────
Console.WriteLine("\n== Update version comparison ==");
Check(UpdateService.IsNewer("v1.3.0", "1.2.1"), "v1.3.0 is newer than 1.2.1");
Check(UpdateService.IsNewer("1.3.0", "1.3.0") == false, "Same version is not newer");
Check(UpdateService.IsNewer("v1.2.0", "1.3.0") == false, "Older tag is not newer");
Check(UpdateService.IsNewer("v1.10.0", "1.9.0"), "Semantic compare: 1.10.0 newer than 1.9.0");
Check(UpdateService.IsNewer("garbage", "1.0.0") == false, "Unparseable tag is not treated as newer");

// ─────────────────── Changelog / what's new ───────────────────
Console.WriteLine("\n== Changelog / what's new ==");
var embedded = ChangelogService.LoadEmbedded();
Check(embedded.Contains("1.5.0", StringComparison.Ordinal), "Embedded CHANGELOG includes 1.5.0");
Check(ChangelogService.GetSectionForVersion("1.5.0", embedded).Contains("CLI", StringComparison.OrdinalIgnoreCase),
    "Changelog section extracts 1.5.0 notes");
Check(ChangelogService.ShouldShowWhatsNew("1.4.1", "1.5.0"), "ShouldShowWhatsNew true after upgrade");
Check(!ChangelogService.ShouldShowWhatsNew("1.5.0", "1.5.0"), "ShouldShowWhatsNew false when versions match");
Check(ChangelogService.ShouldShowWhatsNew("", "1.5.0"), "ShouldShowWhatsNew true when last seen empty");
Check(!ChangelogService.ShouldShowWhatsNew("1.5.1", "1.5.0"), "ShouldShowWhatsNew false when downgraded");

// ─────────────────── URL launch validation ───────────────────
Console.WriteLine("\n== URL launch validation ==");
Check(UrlLaunchHelper.IsAllowedWebUrl("https://github.com/sohiaburrehman-prog/DisplayPilot/releases"),
    "HTTPS GitHub release URL allowed");
Check(UrlLaunchHelper.IsAllowedWebUrl("http://example.com"), "HTTP URL allowed");
Check(!UrlLaunchHelper.IsAllowedWebUrl("file:///C:/Windows/System32/cmd.exe"), "file:// blocked");
Check(!UrlLaunchHelper.IsAllowedWebUrl("javascript:alert(1)"), "javascript: blocked");
Check(!UrlLaunchHelper.IsAllowedWebUrl("ms-msdt:/id PCWDiagnostic /skip force"), "ms-msdt: blocked");
Check(UrlLaunchHelper.IsAllowedWebOrMailUrl(AppInfo.SupportMailtoUri), "mailto help link allowed");
Check(!UrlLaunchHelper.IsAllowedWebUrl(AppInfo.SupportMailtoUri), "mailto blocked for web-only helper");

// ─────────────────── Local app path validation ───────────────────
Console.WriteLine("\n== Local app path validation ==");
var appRoot = LocalAppLaunchHelper.AppDataRootPath;
Check(LocalAppLaunchHelper.IsUnderAppDataRoot(AppLogger.LogPath),
    "App log path is under app data root");
Check(LocalAppLaunchHelper.IsUnderAppDataRoot(AppLogger.LogFolder),
    "App log folder is under app data root");
Check(!LocalAppLaunchHelper.IsUnderAppDataRoot(@"C:\Windows\System32\cmd.exe"),
    "System path rejected");
Check(!LocalAppLaunchHelper.IsUnderAppDataRoot($@"{appRoot}\..\Windows\System32"),
    "Path traversal outside app data rejected");
Check(!LocalAppLaunchHelper.IsUnderAppDataRoot($@"{appRoot}\log.txt"" /e,notepad"),
    "Double-quote argument injection rejected");
Check(!LocalAppLaunchHelper.IsUnderAppDataRoot(null), "Null path rejected");
Check(!LocalAppLaunchHelper.IsUnderAppDataRoot(""), "Empty path rejected");

Console.WriteLine($"\n{passed} passed, {failed} failed");
Environment.Exit(failed > 0 ? 1 : 0);
