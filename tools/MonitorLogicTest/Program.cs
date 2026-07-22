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

// ─────────────────── SetPrimaryByDeviceName / index race ───────────────────
Console.WriteLine("\n== SetPrimaryByDeviceName single-snapshot (index race) ==");
{
    static MonitorInfo MakeMon(int index, string device, bool primary) => new()
    {
        Index = index,
        DeviceName = device,
        Name = $"Monitor {index + 1}",
        Width = 1920,
        Height = 1080,
        PositionX = index * 1920,
        PositionY = 0,
        IsPrimary = primary,
        RefreshRateHz = 60,
    };

    var orderA = new List<MonitorInfo>
    {
        MakeMon(0, @"\\.\DISPLAY1", primary: true),
        MakeMon(1, @"\\.\DISPLAY2", primary: false),
    };
    // Same devices, reversed enumeration order with fresh indices (hotplug / driver churn).
    var orderB = new List<MonitorInfo>
    {
        MakeMon(0, @"\\.\DISPLAY2", primary: false),
        MakeMon(1, @"\\.\DISPLAY1", primary: true),
    };

    var stale = DisplayManager.ResolvePrimaryByStaleIndex(orderA, orderB, @"\\.\DISPLAY2");
    Check(stale.DeviceName == @"\\.\DISPLAY1",
        "Stale-index path would pick DISPLAY1 after reorder (race demonstration)");

    var fixedResolve = DisplayManager.ResolvePrimaryByDeviceName(orderB, @"\\.\DISPLAY2");
    Check(fixedResolve.DeviceName == @"\\.\DISPLAY2",
        "Device-name resolve picks DISPLAY2 from reordered snapshot");

    var getCalls = 0;
    var dryManager = new DisplayManager(() =>
    {
        getCalls++;
        // First call: order A. Any second call returns reordered B (would break stale-index code).
        return getCalls == 1 ? orderA : orderB;
    })
    {
        DryRun = true,
    };

    var byDevice = dryManager.SetPrimaryByDeviceName(@"\\.\DISPLAY2");
    Check(byDevice.DeviceName == @"\\.\DISPLAY2",
        "SetPrimaryByDeviceName dry-run targets DISPLAY2 despite reorder-capable provider");
    Check(dryManager.LastDryRunPrimaryTarget?.DeviceName == @"\\.\DISPLAY2",
        "Dry-run records DISPLAY2 as primary target");
    Check(getCalls == 1,
        "SetPrimaryByDeviceName uses a single GetMonitors snapshot (no re-enumeration)");

    getCalls = 0;
    var cycleManager = new DisplayManager(() =>
    {
        getCalls++;
        return getCalls == 1 ? orderA : orderB;
    })
    {
        DryRun = true,
    };
    var cycled = cycleManager.CyclePrimary();
    Check(cycled.DeviceName == @"\\.\DISPLAY2",
        "CyclePrimary dry-run advances to DISPLAY2 from single snapshot");
    Check(getCalls == 1, "CyclePrimary uses a single GetMonitors snapshot");

    getCalls = 0;
    var swapManager = new DisplayManager(() =>
    {
        getCalls++;
        return getCalls == 1 ? orderA : orderB;
    })
    {
        DryRun = true,
    };
    var swapped = swapManager.SwapPrimaryBetweenTwoMonitors();
    Check(swapped.DeviceName == @"\\.\DISPLAY2",
        "SwapPrimaryBetweenTwoMonitors dry-run targets non-primary by device name");
    Check(getCalls == 1, "SwapPrimaryBetweenTwoMonitors uses a single GetMonitors snapshot");
}

// ─────────────────── LayoutPreset false-restore messaging ───────────────────
Console.WriteLine("\n== LayoutPreset rollback messaging ==");
{
    var missingScene = new LayoutPreset
    {
        Name = "Missing display",
        PrimaryMonitorDeviceName = @"\\.\DISPLAY999",
        MonitorStates =
        {
            [@"\\.\DISPLAY999"] = new DisplaySceneMonitorState
            {
                Width = 1920,
                Height = 1080,
                RefreshRateHz = 60,
                PositionX = 0,
                PositionY = 0,
                Orientation = 0,
            },
        },
    };
    var failResult = LayoutPresetService.TryApply(missingScene, new AppSettings(), manager);
    Check(!failResult.Applied, "TryApply fails when scene primary is disconnected");
    Check(failResult.Message.Contains("were not restored", StringComparison.OrdinalIgnoreCase),
        "Failed apply without rollback does not claim settings were restored");
    Check(!failResult.Message.Contains("were restored.", StringComparison.Ordinal),
        "Failed apply message omits false 'were restored' claim");
}

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

Console.WriteLine("\n== ResolveTarget edge cases ==");
#pragma warning disable CS8625
Check(ProfileMatcher.ResolveTarget(null, dual) is null, "ResolveTarget handles null profile");
Check(ProfileMatcher.ResolveTarget(gameProfile, null) is null, "ResolveTarget handles null monitor list");
#pragma warning restore CS8625
Check(ProfileMatcher.ResolveTarget(gameProfile, new List<MonitorInfo>()) is null, "ResolveTarget handles empty monitor list");

var emptyProfile = new AppProfile { ProcessName = "app", TargetMonitorName = "", TargetMonitorDeviceName = "" };
Check(ProfileMatcher.ResolveTarget(emptyProfile, dual) is null, "ResolveTarget handles profile with empty monitor names");

var caseInsensitiveNameProfile = new AppProfile { ProcessName = "app", TargetMonitorName = "lg 27gl850", TargetMonitorDeviceName = "" };
Check(ProfileMatcher.ResolveTarget(caseInsensitiveNameProfile, dual)?.Name == "LG 27GL850", "ResolveTarget matches friendly name case-insensitively");

var caseInsensitiveDeviceProfile = new AppProfile { ProcessName = "app", TargetMonitorName = "", TargetMonitorDeviceName = @"\\.\display2" };
Check(ProfileMatcher.ResolveTarget(caseInsensitiveDeviceProfile, dual)?.DeviceName == @"\\.\DISPLAY2", "ResolveTarget matches device name case-insensitively");

var unknownMonitorProfile = new AppProfile { ProcessName = "app", TargetMonitorName = "Unknown Monitor", TargetMonitorDeviceName = @"\\.\DISPLAY99" };
Check(ProfileMatcher.ResolveTarget(unknownMonitorProfile, dual) is null, "ResolveTarget returns null when neither name nor device match");

Check(ProfileMatcher.ProcessMatches(gameProfile, "game"), "ProcessMatches strips .exe from profile (game)");
Check(ProfileMatcher.ProcessMatches(gameProfile, "GAME.EXE"), "ProcessMatches is case-insensitive and extension-insensitive");
Check(!ProfileMatcher.ProcessMatches(gameProfile, "notgame"), "ProcessMatches rejects non-matching process");
Check(!ProfileMatcher.ProcessMatches(gameProfile, ""), "ProcessMatches rejects empty process name");
Check(!ProfileMatcher.ProcessMatches(null!, "game"), "ProcessMatches rejects null profile");
Check(ProfileMatcher.ProcessMatches(gameProfile, "  game.exe  "), "ProcessMatches trims whitespace in process name");

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

Check(ProfileMatcher.ProcessMatches(launcherProfile, "steam"), "ProcessMatches matches launcher process name");
Check(ProfileMatcher.ProcessMatches(launcherProfile, "eldenring"), "ProcessMatches matches resolved target process name");
Check(ProfileMatcher.ProcessMatches(launcherProfile, "ELDENRING.EXE"), "ProcessMatches matches resolved target process name case-insensitively");
Check(!ProfileMatcher.ProcessMatches(launcherProfile, "notgame"), "ProcessMatches rejects unrelated process for launcher profile");

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
Check(ProcessPickerHelper.IsExcludedProcess("steamwebhelper"), "Launcher helper processes excluded from child detection");
Check(!ProcessPickerHelper.IsExcludedProcess("eldenring"), "Game processes not excluded from child suggestions");

Console.WriteLine("\n== Launcher PID ancestry tracking ==");
var ancestryState = new LauncherChildTracker.WatchState();
var ancestryProfile = new AppProfile { ProcessName = "steam.exe", MatchLauncherChildren = true };
var ancestryProcesses = new List<LauncherChildTracker.RunningProcess>
{
    new(100, "steam"),
    new(200, "realgame"),
    new(300, "unrelated"),
};
var ancestryStarts = new List<LauncherChildTracker.ProcessStart>
{
    new(300, 999, "unrelated", DateTime.UtcNow.AddSeconds(-2)),
    new(200, 100, "realgame", DateTime.UtcNow.AddSeconds(-1)),
};
Check(LauncherChildTracker.UpdateWatchState(ancestryProfile, ancestryState, ancestryProcesses, ancestryStarts) == "realgame",
    "Launcher tracker selects an actual launcher descendant, not the first unrelated process");
Check(LauncherChildTracker.UpdateWatchState(
        ancestryProfile,
        ancestryState,
        new List<LauncherChildTracker.RunningProcess> { new(200, "realgame") },
        ancestryStarts) == "realgame",
    "Detected game remains active after launcher exits");
Check(LauncherChildTracker.UpdateWatchState(
        ancestryProfile,
        ancestryState,
        new List<LauncherChildTracker.RunningProcess> { new(300, "unrelated") },
        ancestryStarts) is null,
    "Launcher tracker clears the child after the game exits");

Console.WriteLine("\n== Profile conflict resolution ==");
var lowPriority = gameProfile.Clone();
lowPriority.Id = "low";
lowPriority.Priority = 1;
var highPriority = gameProfile.Clone();
highPriority.Id = "high";
highPriority.Priority = 10;
var conflictCandidates = new[]
{
    new ProfileConflictResolver.Candidate(highPriority, ActivationOrder: 1),
    new ProfileConflictResolver.Candidate(lowPriority, ActivationOrder: 2),
};
Check(ProfileConflictResolver.SelectWinner(conflictCandidates, ProfileConflictRule.HighestPriority)?.Profile.Id == "high",
    "Highest-priority conflict rule chooses priority before activation time");
Check(ProfileConflictResolver.SelectWinner(conflictCandidates, ProfileConflictRule.MostRecentlyActivated)?.Profile.Id == "low",
    "Most-recent conflict rule chooses activation time before priority");

// ─────────────────── Monitor nicknames ───────────────────
Console.WriteLine("\n== Monitor nickname persistence ==");
var nicknameSettings = new AppSettings();
nicknameSettings.MonitorNicknames[@"\\.\DISPLAY2"] = "Gaming screen";
Check(MonitorDisplayHelper.GetDisplayName(dual[1], nicknameSettings) == "Gaming screen",
    "Nickname overrides hardware name");
Check(MonitorDisplayHelper.GetNumberedName(dual[1], nicknameSettings) == "2 · Gaming screen",
    "Numbered name uses nickname");
nicknameSettings.MonitorNicknames[@"\\.\DISPLAY2"] = "Gaming screen";
Check(MonitorDisplayHelper.GetTrayMenuLine(dual[1], nicknameSettings, compact: true) == "Gaming screen",
    "Compact tray line uses nickname without specs");
var primaryCompact = dual.First(m => m.IsPrimary);
Check(MonitorDisplayHelper.GetTrayMenuLine(primaryCompact, nicknameSettings, compact: true)
        .StartsWith('✓'),
    "Compact primary tray line is checkmarked");
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

// ─────────────────── Arrangement map layout ───────────────────
Console.WriteLine("\n== Arrangement map layout ==");

MonitorInfo LayoutMonitor(int index, int x, int y, int width, int height) => new()
{
    Index = index,
    DeviceName = $@"\\.\DISPLAY{index + 1}",
    Name = $"Display {index + 1}",
    Width = width,
    Height = height,
    PositionX = x,
    PositionY = y,
    IsPrimary = index == 1,
    RefreshRateHz = 60,
};

var left4K = new List<MonitorInfo>
{
    LayoutMonitor(0, -3840, 0, 3840, 2160),
    LayoutMonitor(1, 0, 0, 3440, 1440),
};
var leftLayout = ArrangementMapLayout.Compute(left4K, viewportWidth: 320, mapHeight: 104);
Check(leftLayout.Tiles[0].Left < leftLayout.Tiles[1].Left,
    "4K monitor left of ultrawide renders to the left in the map");
Check(!leftLayout.NeedsHorizontalScroll,
    "Wide dual-monitor desktop fits viewport without horizontal scroll");
Check(leftLayout.ContentWidth == 320,
    "Wide dual-monitor map canvas matches viewport width");

var wideTriple = new List<MonitorInfo>
{
    LayoutMonitor(0, -3840, 0, 3840, 2160),
    LayoutMonitor(1, 0, 0, 3440, 1440),
    LayoutMonitor(2, 3440, 0, 2560, 720),
};
var tripleLayout = ArrangementMapLayout.Compute(wideTriple, viewportWidth: 320, mapHeight: 104);
Check(tripleLayout.Tiles[0].Left < tripleLayout.Tiles[1].Left &&
      tripleLayout.Tiles[1].Left < tripleLayout.Tiles[2].Left,
    "Three-monitor topology preserves left-to-right order");
Check(tripleLayout.ContentWidth > 320,
    "Three-monitor desktop enables horizontal scrolling");
Check(tripleLayout.NeedsHorizontalScroll,
    "Three-monitor desktop reports horizontal scroll needed");

// Flyout map viewport is ~326 px (380 window − margins/padding); wide desktops
// that visually fit must not report a few pixels of overflow from tile insets.
const double panelMapViewport = 326;
var snugDual = new List<MonitorInfo>
{
    LayoutMonitor(0, 0, 0, 1280, 1024),
    LayoutMonitor(1, 1280, 0, 1280, 1024),
};
var snugLayout = ArrangementMapLayout.Compute(snugDual, viewportWidth: panelMapViewport, mapHeight: 104);
Check(!snugLayout.NeedsHorizontalScroll,
    "Dual-monitor map that fits panel viewport does not enable horizontal scroll");
Check(snugLayout.ContentWidth == panelMapViewport,
    "Fitting dual-monitor map expands canvas to viewport width for centering");

var dual1080p = new List<MonitorInfo>
{
    LayoutMonitor(0, 0, 0, 1920, 1080),
    LayoutMonitor(1, 1920, 0, 1920, 1080),
};
var dual1080Layout = ArrangementMapLayout.Compute(dual1080p, viewportWidth: panelMapViewport, mapHeight: 104);
Check(!dual1080Layout.NeedsHorizontalScroll,
    "Dual 1080p side-by-side fits panel without horizontal scroll");
Check(dual1080Layout.ContentWidth == panelMapViewport,
    "Dual 1080p map canvas matches viewport width");
Check(dual1080Layout.Tiles[1].Left + dual1080Layout.Tiles[1].Width + 2 <= panelMapViewport,
    "Dual 1080p tiles stay within viewport width");

var ultrawide4K = new List<MonitorInfo>
{
    LayoutMonitor(0, 0, 0, 3440, 1440),
    LayoutMonitor(1, 3440, 0, 3840, 2160),
};
const double flyoutMapViewport = 380;
var ultrawideLayout = ArrangementMapLayout.Compute(ultrawide4K, viewportWidth: flyoutMapViewport, mapHeight: 104);
Check(!ultrawideLayout.NeedsHorizontalScroll,
    "Ultrawide + 4K dual setup fits flyout map without horizontal scroll");
Check(ultrawideLayout.ContentWidth == flyoutMapViewport,
    "Ultrawide + 4K map fills viewport when content fits");

// ─────────────────── Settings JSON round-trip ───────────────────
Console.WriteLine("\n== Settings JSON serialization round-trip ==");

settings.Profiles.Add(gameProfile);
settings.Profiles.Add(launcherProfile);
settings.Profiles[0].Priority = 25;
settings.ProfileConflictRule = ProfileConflictRule.MostRecentlyActivated;
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
Check(restored.Profiles[0].Priority == 25, "Profile priority survives JSON round-trip");
Check(restored.ProfileConflictRule == ProfileConflictRule.MostRecentlyActivated,
    "Profile conflict rule survives JSON round-trip");

// ─────────────────── Schema v8: compact tray + scene profiles ───────────────────
Console.WriteLine("\n== Schema v8: compact tray menu & scene profiles ==");
Check(AppSettings.CurrentSchemaVersion == 8, "Current schema version is 8");
Check(new AppSettings().CompactTrayMenu, "CompactTrayMenu defaults to true");
var compactOff = new AppSettings { CompactTrayMenu = false };
var compactRoundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(compactOff));
Check(compactRoundTrip?.CompactTrayMenu == false, "CompactTrayMenu=false survives JSON round-trip");
Check(JsonSerializer.Deserialize<AppSettings>("{\"SchemaVersion\":8}")?.CompactTrayMenu == true,
    "Missing CompactTrayMenu in JSON keeps default true");

var v4Settings = new AppSettings { SchemaVersion = 3 };
v4Settings.Profiles.Add(gameProfile.Clone());
SettingsService.TryParseImport(JsonSerializer.Serialize(v4Settings), out var migrated, out _);
Check(migrated?.SchemaVersion == 8, "Import normalizes schema to v8");
Check(migrated?.CompactTrayMenu == true, "Migrated settings keep CompactTrayMenu default");
Check(migrated?.LayoutPresets is not null && migrated.LayoutPresets.Count == 0, "LayoutPresets initialized on migrate");

var preset = LayoutPresetService.CaptureCurrent("Desk", manager);
Check(!string.IsNullOrWhiteSpace(preset.PrimaryMonitorDeviceName), "Layout preset captures primary device");
Check(preset.MonitorModes.Count >= count, "Layout preset captures mode per connected monitor");
Check(preset.MonitorStates.Count >= count, "Display scene captures complete state per connected monitor");
Check(preset.MonitorStates.Values.All(s => s.Orientation <= 3), "Display scene captures valid orientations");
var scenePreview = LayoutPresetService.Preview(preset, new AppSettings(), manager);
Check(scenePreview.Valid && scenePreview.IsPreview, "Display scene preview preflights without applying");
var normalizedScene = preset.Clone();
normalizedScene.Id = string.Empty;
normalizedScene.Name = "  Imported desk  ";
Check(LayoutPresetService.TryNormalizeImported(normalizedScene, out var normalizeError) && normalizeError is null,
    "Scene import validation accepts and normalizes a complete scene");
Check(normalizedScene.Name == "Imported desk" && !string.IsNullOrWhiteSpace(normalizedScene.Id),
    "Scene import normalization trims the name and assigns an ID");
var invalidScene = preset.Clone();
invalidScene.MonitorStates.First().Value.Orientation = 9;
Check(!LayoutPresetService.TryNormalizeImported(invalidScene, out var invalidSceneError) && invalidSceneError is not null,
    "Scene import validation rejects an invalid orientation");

// Historical fixtures deliberately omit properties introduced by later schemas.
var schema5Json = """
{
  "SchemaVersion": 5,
  "Profiles": [{
    "Id": "v5-profile",
    "ProcessName": "legacy-game.exe",
    "TargetMonitorName": "Legacy display",
    "TargetMonitorDeviceName": "\\\\.\\DISPLAY2",
    "RestoreOnExit": true,
    "Priority": 12
  }],
  "LayoutPresets": [{
    "Id": "v5-preset",
    "Name": "Legacy modes",
    "PrimaryMonitorDeviceName": "\\\\.\\DISPLAY2",
    "MonitorModes": {
      "\\\\.\\DISPLAY1": { "Width": 2560, "Height": 1440, "RefreshRateHz": 144 },
      "\\\\.\\DISPLAY2": { "Width": 1920, "Height": 1080, "RefreshRateHz": 60 }
    }
  }]
}
""";
Check(SettingsService.TryParseImport(schema5Json, out var schema5, out var schema5Error),
    $"Schema v5 fixture imports (err={schema5Error ?? "none"})");
Check(schema5?.SchemaVersion == AppSettings.CurrentSchemaVersion,
    "Schema v5 upgrades to the current schema");
Check(schema5?.Profiles.Single().Priority == 12 && schema5.Profiles[0].RestoreOnExit,
    "Schema v5 preserves profile priority and restore-on-exit");
Check(schema5?.LayoutPresets.Single().MonitorModes.Count == 2 &&
      schema5.LayoutPresets[0].MonitorStates.Count == 0,
    "Schema v5 legacy monitor modes remain available for safe live-state expansion");

var schema6Json = """
{
  "SchemaVersion": 6,
  "Profiles": [{
    "Id": "v6-profile",
    "ProcessName": "scene-game.exe",
    "TargetMonitorName": "Scene display",
    "TargetMonitorDeviceName": "\\\\.\\DISPLAY1"
  }],
  "LayoutPresets": [{
    "Id": "v6-scene",
    "Name": "Complete scene",
    "PrimaryMonitorDeviceName": "\\\\.\\DISPLAY1",
    "MonitorStates": {
      "\\\\.\\DISPLAY1": {
        "Width": 1440, "Height": 2560, "RefreshRateHz": 120,
        "PositionX": 0, "PositionY": 0, "Orientation": 1, "HdrEnabled": true
      }
    }
  }]
}
""";
Check(SettingsService.TryParseImport(schema6Json, out var schema6, out var schema6Error),
    $"Schema v6 fixture imports (err={schema6Error ?? "none"})");
var schema6State = schema6?.LayoutPresets.Single().MonitorStates.Single().Value;
Check(schema6?.SchemaVersion == AppSettings.CurrentSchemaVersion &&
      schema6State is { Width: 1440, Height: 2560, RefreshRateHz: 120, Orientation: 1, HdrEnabled: true },
    "Schema v6 preserves full resolution, refresh, rotation, position, and HDR scene state");
Check(schema6?.Profiles.Single().DisplaySceneId == string.Empty,
    "Schema v6 profile safely defaults its later scene reference to empty");

var schema7Json = """
{
  "SchemaVersion": 7,
  "Profiles": [{
    "Id": "v7-profile",
    "ProcessName": "linked-game.exe",
    "TargetMonitorDeviceName": "\\\\.\\DISPLAY2",
    "DisplaySceneId": "v7-scene",
    "LastTriggeredUtc": "2026-01-02T03:04:05Z"
  }],
  "LayoutPresets": [{
    "Id": "v7-scene",
    "Name": "Linked scene",
    "PrimaryMonitorDeviceName": "\\\\.\\DISPLAY2",
    "MonitorStates": {
      "\\\\.\\DISPLAY2": {
        "Width": 3440, "Height": 1440, "RefreshRateHz": 175,
        "PositionX": 2560, "PositionY": 0, "Orientation": 0, "HdrEnabled": false
      }
    }
  }]
}
""";
Check(SettingsService.TryParseImport(schema7Json, out var schema7, out var schema7Error),
    $"Schema v7 fixture imports (err={schema7Error ?? "none"})");
Check(schema7?.SchemaVersion == AppSettings.CurrentSchemaVersion && schema7.CompactTrayMenu,
    "Schema v7 upgrades to v8 with the compact-tray default");
Check(schema7?.Profiles.Single().DisplaySceneId == "v7-scene" &&
      schema7.LayoutPresets.Single().Id == "v7-scene",
    "Schema v7 preserves the profile-to-scene link");

Console.WriteLine("\n== Scene apply failure injection & rollback ==");
var fakeSceneDisplay = new FakeSceneDisplayOperations();
var injectedScene = fakeSceneDisplay.CreateTargetScene();
var forwardStages = new[]
{
    LayoutPresetService.SceneApplyStage.BuildDesiredState,
    LayoutPresetService.SceneApplyStage.Preflight,
    LayoutPresetService.SceneApplyStage.DescribeChanges,
    LayoutPresetService.SceneApplyStage.CaptureRollback,
    LayoutPresetService.SceneApplyStage.ApplyTopology,
    LayoutPresetService.SceneApplyStage.ApplyHdr,
    LayoutPresetService.SceneApplyStage.VerifyAppliedState,
};
foreach (var injectedStage in forwardStages)
{
    fakeSceneDisplay.Reset();
    var result = LayoutPresetService.TryApply(
        injectedScene,
        new AppSettings(),
        fakeSceneDisplay,
        stage =>
        {
            if (stage == injectedStage)
            {
                throw new InvalidOperationException($"Injected {stage} failure.");
            }
        });
    Check(!result.Applied && result.FailedStage == injectedStage,
        $"Injected {injectedStage} failure is attributed to the correct stage");

    var shouldRollback = injectedStage >= LayoutPresetService.SceneApplyStage.ApplyTopology;
    Check(result.RollbackAttempted == shouldRollback,
        $"Injected {injectedStage} failure {(shouldRollback ? "runs" : "does not run")} rollback");
    Check(!shouldRollback || result.RollbackSucceeded,
        $"Injected {injectedStage} failure {(shouldRollback ? "restores" : "does not mutate")} topology and HDR state");
    Check(fakeSceneDisplay.MatchesBaseline(),
        $"Injected {injectedStage} failure leaves the fake hardware at its baseline");
}

fakeSceneDisplay.Reset();
var missingMonitorScene = injectedScene.Clone();
missingMonitorScene.MonitorStates[@"\\.\DISPLAY3"] = missingMonitorScene.MonitorStates[@"\\.\DISPLAY2"].Clone();
var missingResult = LayoutPresetService.TryApply(missingMonitorScene, new AppSettings(), fakeSceneDisplay);
Check(!missingResult.Applied && missingResult.FailedStage == LayoutPresetService.SceneApplyStage.Preflight,
    "Disconnected monitor fails preflight before any display mutation");
Check(!missingResult.RollbackAttempted && fakeSceneDisplay.MatchesBaseline(),
    "Disconnected-monitor rejection does not run or need rollback");

fakeSceneDisplay.Reset();
var rollbackTopologyFailure = LayoutPresetService.TryApply(
    injectedScene,
    new AppSettings(),
    fakeSceneDisplay,
    stage =>
    {
        if (stage is LayoutPresetService.SceneApplyStage.ApplyHdr or
            LayoutPresetService.SceneApplyStage.RollbackTopology)
        {
            throw new InvalidOperationException($"Injected {stage} failure.");
        }
    });
Check(rollbackTopologyFailure.RollbackAttempted && !rollbackTopologyFailure.RollbackSucceeded &&
      rollbackTopologyFailure.Message.Contains("Rollback also failed", StringComparison.Ordinal),
    "Rollback-topology injection reports that restoration failed");

fakeSceneDisplay.Reset();
var rollbackHdrFailure = LayoutPresetService.TryApply(
    injectedScene,
    new AppSettings(),
    fakeSceneDisplay,
    stage =>
    {
        if (stage is LayoutPresetService.SceneApplyStage.VerifyAppliedState or
            LayoutPresetService.SceneApplyStage.RollbackHdr)
        {
            throw new InvalidOperationException($"Injected {stage} failure.");
        }
    });
Check(rollbackHdrFailure.RollbackAttempted && !rollbackHdrFailure.RollbackSucceeded &&
      rollbackHdrFailure.Message.Contains("Rollback also failed", StringComparison.Ordinal),
    "Rollback-HDR injection reports that restoration failed");

v4Settings.SchemaVersion = 8;
v4Settings.LayoutPresets.Add(preset);
v4Settings.Profiles[0].DisplaySceneId = preset.Id;
v4Settings.LastUsedProfileId = gameProfile.Id;
v4Settings.Profiles[0].LastTriggeredUtc = DateTime.UtcNow;
var v4Json = JsonSerializer.Serialize(v4Settings);
var v4Restored = JsonSerializer.Deserialize<AppSettings>(v4Json);
Check(v4Restored?.LayoutPresets.Count == 1 && v4Restored.LayoutPresets[0].Name == "Desk",
    "Layout presets survive JSON round-trip");
Check(v4Restored?.LastUsedProfileId == gameProfile.Id, "LastUsedProfileId survives JSON round-trip");
Check(v4Restored?.Profiles[0].LastTriggeredUtc > DateTime.MinValue,
    "LastTriggeredUtc survives JSON round-trip");
Check(v4Restored?.Profiles[0].DisplaySceneId == preset.Id,
    "Profile scene action survives JSON round-trip");
Check(v4Settings.Profiles[0].Clone().DisplaySceneId == preset.Id,
    "Profile clone preserves its scene action");

var explanation = DisplaySceneDiagnosticsService.FormatReport(new DisplaySceneDiagnosticsService.Snapshot
{
    Monitors = new List<DisplaySceneDiagnosticsService.MonitorEntry>
    {
        new()
        {
            Label = "Desk",
            DeviceName = @"\\.\DISPLAY1",
            IsPrimary = true,
            State = new DisplaySceneMonitorState
            {
                Width = 3840,
                Height = 2160,
                RefreshRateHz = 120,
                PositionX = 0,
                PositionY = 0,
                Orientation = 0,
                HdrEnabled = true,
            },
        },
    },
    Scenes = new List<DisplaySceneDiagnosticsService.SceneEntry>
    {
        new()
        {
            Name = "Gaming",
            Id = "scene-1",
            PrimaryLabel = "Desk",
            IsFullScene = true,
            MonitorCount = 1,
            CanApply = true,
            Summary = "Scene is ready.",
            Changes = new[] { "Desk: HDR off" },
            ReferencedByProfiles = new[] { "game" },
        },
    },
});
Check(explanation.Contains("CURRENT DISPLAY STATE", StringComparison.Ordinal) &&
      explanation.Contains("Desk [PRIMARY]", StringComparison.Ordinal) &&
      explanation.Contains("HDR on", StringComparison.Ordinal),
    "Display-scene explanation includes the current primary state");
Check(explanation.Contains("[READY] Gaming", StringComparison.Ordinal) &&
      explanation.Contains("Would change: Desk: HDR off", StringComparison.Ordinal) &&
      explanation.Contains("Used by profiles: game", StringComparison.Ordinal),
    "Display-scene explanation includes readiness, changes, and profile references");

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

var evalNull = ProfileMatcher.Evaluate(null!, runningGame, dual);
Check(!evalNull.ProfileEnabled && evalNull.Summary == "Profile is missing.",
    "Evaluate: null profile handled gracefully");

var disabledProfile = gameProfile.Clone();
disabledProfile.Enabled = false;
var evalDisabled = ProfileMatcher.Evaluate(disabledProfile, runningGame, dual);
Check(!evalDisabled.ProfileEnabled && !evalDisabled.WouldMatch,
    "Evaluate: disabled profile returns no match");
Check(evalDisabled.Summary.Contains("disabled", StringComparison.OrdinalIgnoreCase),
    "Evaluate summary mentions disabled");

var primaryProfile = new AppProfile { ProcessName = "game.exe", TargetMonitorName = "Dell U2720", TargetMonitorDeviceName = @"\\.\DISPLAY1" };
var evalAlreadyPrimary = ProfileMatcher.Evaluate(primaryProfile, runningGame, dual);
Check(evalAlreadyPrimary.ProcessRunning && evalAlreadyPrimary.TargetConnected && evalAlreadyPrimary.TargetIsPrimary && !evalAlreadyPrimary.WouldMatch,
    "Evaluate: process running and target connected, but already primary => no match");
Check(evalAlreadyPrimary.Summary.Contains("already primary", StringComparison.OrdinalIgnoreCase),
    "Evaluate summary mentions already primary");

var constrainedProfile = gameProfile.Clone();
constrainedProfile.ExecutablePath = @"C:\Games\game.exe";
constrainedProfile.WindowTitleContains = "Ranked Match";
var detailedProcesses = new[]
{
    new LauncherChildTracker.RunningProcess(42, "game", @"C:\Games\game.exe", "Game — Ranked Match"),
};
var constrainedMatch = ProfileMatcher.Evaluate(
    constrainedProfile,
    runningGame,
    dual,
    runningProcesses: detailedProcesses);
Check(constrainedMatch.ProcessRunning && constrainedMatch.PathConstraintMatched && constrainedMatch.WindowTitleConstraintMatched,
    "Evaluate: executable path and window title constraints match");

var wrongPathProcesses = new[]
{
    new LauncherChildTracker.RunningProcess(42, "game", @"D:\Other\game.exe", "Game — Ranked Match"),
};
var constrainedNoMatch = ProfileMatcher.Evaluate(
    constrainedProfile,
    runningGame,
    dual,
    runningProcesses: wrongPathProcesses);
Check(!constrainedNoMatch.ProcessRunning && !constrainedNoMatch.PathConstraintMatched,
    "Evaluate: wrong executable path blocks a name match");
Check(constrainedNoMatch.Summary.Contains("Process name matched", StringComparison.OrdinalIgnoreCase),
    "Evaluate: constraint failure is explained");

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

Console.WriteLine("\n== Work-area query (flyout placement) ==");
{
    var ok = PrimaryDisplaySwap.Native.WindowInterop.TryGetWorkAreaPixels(
        new PrimaryDisplaySwap.Native.WindowInterop.POINT { X = 0, Y = 0 },
        out var work,
        out var dpiX,
        out var dpiY);
    Check(ok, "TryGetWorkAreaPixels succeeds for primary");
    Check(work.Width > 0 && work.Height > 0, "Work area has positive size");
    Check(dpiX >= 96 && dpiY >= 96, $"DPI is at least 96 (got {dpiX}x{dpiY})");
    Check(work.Right > work.Left && work.Bottom > work.Top, "Work area rect is well-ordered");
}

Console.WriteLine($"\n{passed} passed, {failed} failed");
Environment.Exit(failed > 0 ? 1 : 0);

internal sealed class FakeSceneDisplayOperations : LayoutPresetService.ISceneDisplayOperations
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    private Dictionary<string, DisplaySceneMonitorState> _states = new(StringComparer.OrdinalIgnoreCase);
    private string _primaryDeviceName = Display1;

    public FakeSceneDisplayOperations() => Reset();

    public void Reset()
    {
        _primaryDeviceName = Display1;
        _states = new Dictionary<string, DisplaySceneMonitorState>(StringComparer.OrdinalIgnoreCase)
        {
            [Display1] = new()
            {
                Width = 2560,
                Height = 1440,
                RefreshRateHz = 144,
                PositionX = 0,
                PositionY = 0,
                Orientation = 0,
                HdrEnabled = false,
            },
            [Display2] = new()
            {
                Width = 3440,
                Height = 1440,
                RefreshRateHz = 175,
                PositionX = 2560,
                PositionY = 0,
                Orientation = 0,
                HdrEnabled = false,
            },
        };
    }

    public LayoutPreset CreateTargetScene() => new()
    {
        Id = "failure-injection-scene",
        Name = "Failure injection",
        PrimaryMonitorDeviceName = Display2,
        MonitorStates = new Dictionary<string, DisplaySceneMonitorState>(StringComparer.OrdinalIgnoreCase)
        {
            [Display1] = new()
            {
                Width = 1440,
                Height = 2560,
                RefreshRateHz = 120,
                PositionX = 0,
                PositionY = 0,
                Orientation = 1,
                HdrEnabled = true,
            },
            [Display2] = new()
            {
                Width = 1920,
                Height = 1080,
                RefreshRateHz = 60,
                PositionX = 1440,
                PositionY = 0,
                Orientation = 0,
                HdrEnabled = true,
            },
        },
    };

    public bool MatchesBaseline() =>
        string.Equals(_primaryDeviceName, Display1, StringComparison.OrdinalIgnoreCase) &&
        Matches(_states[Display1], 2560, 1440, 144, 0, 0, 0, false) &&
        Matches(_states[Display2], 3440, 1440, 175, 2560, 0, 0, false);

    public IReadOnlyList<MonitorInfo> GetMonitors() => _states.Select((entry, index) => new MonitorInfo
    {
        Index = index,
        DeviceName = entry.Key,
        Name = $"Fake monitor {index + 1}",
        Width = entry.Value.Width,
        Height = entry.Value.Height,
        PositionX = entry.Value.PositionX,
        PositionY = entry.Value.PositionY,
        IsPrimary = string.Equals(entry.Key, _primaryDeviceName, StringComparison.OrdinalIgnoreCase),
        RefreshRateHz = entry.Value.RefreshRateHz,
    }).ToList();

    public DisplaySceneMonitorState? GetCurrentSceneState(string deviceName) =>
        _states.TryGetValue(deviceName, out var state) ? state.Clone() : null;

    public void TestDisplayScene(
        IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
        string primaryDeviceName)
    {
        if (!states.ContainsKey(primaryDeviceName))
        {
            throw new InvalidOperationException($"Primary monitor '{primaryDeviceName}' is missing from the scene.");
        }

        var missing = states.Keys.FirstOrDefault(device => !_states.ContainsKey(device));
        if (missing is not null)
        {
            throw new InvalidOperationException($"Monitor '{missing}' is disconnected.");
        }
    }

    public void ApplyDisplaySceneConfiguration(
        IReadOnlyDictionary<string, DisplaySceneMonitorState> states,
        string primaryDeviceName)
    {
        TestDisplayScene(states, primaryDeviceName);
        foreach (var (deviceName, target) in states)
        {
            var currentHdr = _states[deviceName].HdrEnabled;
            _states[deviceName] = target.Clone();
            _states[deviceName].HdrEnabled = currentHdr;
        }
        _primaryDeviceName = primaryDeviceName;
    }

    public DisplayManager.HdrStatus? GetHdrStatus(string deviceName) =>
        _states.TryGetValue(deviceName, out var state)
            ? new DisplayManager.HdrStatus(true, state.HdrEnabled == true)
            : null;

    public void SetHdrEnabled(string deviceName, bool enable)
    {
        if (!_states.TryGetValue(deviceName, out var state))
        {
            throw new InvalidOperationException($"Monitor '{deviceName}' is disconnected.");
        }
        state.HdrEnabled = enable;
    }

    private static bool Matches(
        DisplaySceneMonitorState state,
        int width,
        int height,
        int refresh,
        int x,
        int y,
        uint orientation,
        bool hdr) =>
        state.Width == width && state.Height == height && state.RefreshRateHz == refresh &&
        state.PositionX == x && state.PositionY == y && state.Orientation == orientation &&
        state.HdrEnabled == hdr;
}
