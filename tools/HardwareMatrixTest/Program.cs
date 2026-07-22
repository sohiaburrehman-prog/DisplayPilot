using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

var runLive = args.Contains("--confirm-live", StringComparer.OrdinalIgnoreCase);
var manager = new DisplayManager();
var monitors = manager.GetMonitors();
var baselinePrimary = monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName ?? string.Empty;
var baseline = monitors
    .Select(m => (m.DeviceName, State: manager.GetCurrentSceneState(m.DeviceName)))
    .Where(entry => entry.State is not null)
    .ToDictionary(entry => entry.DeviceName, entry => entry.State!, StringComparer.OrdinalIgnoreCase);
var passed = 0;
var failed = 0;
var skipped = 0;

void Pass(string message)
{
    passed++;
    Console.WriteLine($"PASS: {message}");
}

void Fail(string message)
{
    failed++;
    Console.WriteLine($"FAIL: {message}");
}

void Skip(string message)
{
    skipped++;
    Console.WriteLine($"SKIP: {message}");
}

bool RestoreBaseline(string context)
{
    try
    {
        manager.ApplyDisplaySceneConfiguration(baseline, baselinePrimary);
        foreach (var (deviceName, state) in baseline)
        {
            if (!state.HdrEnabled.HasValue)
            {
                continue;
            }

            var hdr = manager.GetHdrStatus(deviceName);
            if (hdr?.Supported == true && hdr.Enabled != state.HdrEnabled.Value)
            {
                manager.SetHdrEnabled(deviceName, state.HdrEnabled.Value);
            }
        }
        Thread.Sleep(1200);
        var primary = manager.GetMonitors().FirstOrDefault(m => m.IsPrimary)?.DeviceName;
        var restored = string.Equals(primary, baselinePrimary, StringComparison.OrdinalIgnoreCase) &&
            baseline.All(entry => StatesMatch(manager.GetCurrentSceneState(entry.Key), entry.Value));
        if (!restored)
        {
            Fail($"{context}: baseline restore verification failed");
            return false;
        }
        return true;
    }
    catch (Exception ex)
    {
        Fail($"{context}: baseline restore failed — {ex.Message}");
        return false;
    }
}

Console.WriteLine("DisplayPilot 1.8 hardware matrix");
Console.WriteLine($"Live mutation: {(runLive ? "ENABLED" : "disabled (pass --confirm-live)")}");
foreach (var monitor in monitors)
{
    var state = baseline.GetValueOrDefault(monitor.DeviceName);
    Console.WriteLine(
        $"  {monitor.NumberedName}: {state?.Width}x{state?.Height} @ {state?.RefreshRateHz} Hz, " +
        $"orientation={state?.Orientation}, HDR={state?.HdrEnabled?.ToString() ?? "unknown"}, " +
        $"primary={monitor.IsPrimary}");
}

if (monitors.Count == 0 || baseline.Count != monitors.Count || string.IsNullOrWhiteSpace(baselinePrimary))
{
    Fail("Could not capture a complete connected-display baseline");
    Finish();
    return;
}
Pass($"Captured a complete {baseline.Count}-monitor rollback baseline");

try
{
    manager.TestDisplayScene(baseline, baselinePrimary);
    Pass("Current mixed-resolution and refresh-rate scene passes native preflight");
}
catch (Exception ex)
{
    Fail($"Current scene preflight failed — {ex.Message}");
}

if (baseline.Values.Select(state => (state.Width, state.Height)).Distinct().Count() > 1)
{
    Pass("Mixed-resolution hardware is active");
}
else
{
    Skip("Mixed-resolution hardware is not present");
}

if (baseline.Values.Select(state => state.RefreshRateHz).Distinct().Count() > 1)
{
    Pass("Mixed-refresh hardware is active");
}
else
{
    Skip("Mixed-refresh hardware is not present");
}

var disconnected = baseline.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
disconnected[@"\\.\DISPLAY999"] = baseline.Values.First().Clone();
try
{
    manager.TestDisplayScene(disconnected, baselinePrimary);
    Fail("Disconnected-monitor scene unexpectedly passed preflight");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase))
{
    Pass("Disconnected-monitor scene is rejected before commit");
}

var invalidRotation = baseline.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
invalidRotation.Values.First().Orientation = 4;
try
{
    manager.TestDisplayScene(invalidRotation, baselinePrimary);
    Fail("Invalid rotation unexpectedly passed preflight");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("invalid orientation", StringComparison.OrdinalIgnoreCase))
{
    Pass("Invalid rotation is rejected before commit");
}

if (!runLive)
{
    Skip("Primary-switch rollback live test");
    Skip("90-degree rotation and rollback live test");
    Skip("HDR toggle and rollback live test");
    Finish();
    return;
}

try
{
    if (monitors.Count > 1)
    {
        var alternate = monitors.First(m => !string.Equals(m.DeviceName, baselinePrimary, StringComparison.OrdinalIgnoreCase));
        var translated = baseline.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var origin = translated[alternate.DeviceName];
        foreach (var state in translated.Values)
        {
            state.PositionX -= origin.PositionX;
            state.PositionY -= origin.PositionY;
        }
        manager.ApplyDisplaySceneConfiguration(translated, alternate.DeviceName);
        Thread.Sleep(1200);
        var newPrimary = manager.GetMonitors().FirstOrDefault(m => m.IsPrimary)?.DeviceName;
        if (string.Equals(newPrimary, alternate.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            Pass("Primary-monitor scene committed on mixed hardware");
        }
        else
        {
            Fail("Primary-monitor scene committed but verification found the wrong primary");
        }
    }
    else
    {
        Skip("Primary-switch live test requires at least two monitors");
    }
}
catch (Exception ex)
{
    Fail($"Primary-monitor live test failed — {ex.Message}");
}
finally
{
    if (RestoreBaseline("Primary-monitor live test"))
    {
        Pass("Primary-monitor live test restored the exact baseline");
    }
}

try
{
    var rotationTarget = monitors.FirstOrDefault(m => !m.IsPrimary) ?? monitors[0];
    var rotated = baseline.ToDictionary(entry => entry.Key, entry => entry.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    var target = rotated[rotationTarget.DeviceName];
    (target.Width, target.Height) = (target.Height, target.Width);
    target.Orientation = (target.Orientation + 1) % 4;
    manager.TestDisplayScene(rotated, baselinePrimary);
    manager.ApplyDisplaySceneConfiguration(rotated, baselinePrimary);
    Thread.Sleep(1200);
    var applied = manager.GetCurrentSceneState(rotationTarget.DeviceName);
    if (applied?.Orientation == target.Orientation && applied.Width == target.Width && applied.Height == target.Height)
    {
        Pass($"90-degree rotation committed on {rotationTarget.NumberedName}");
    }
    else
    {
        Fail($"Rotation commit verification failed on {rotationTarget.NumberedName}");
    }
}
catch (Exception ex)
{
    Fail($"Rotation live test failed — {ex.Message}");
}
finally
{
    if (RestoreBaseline("Rotation live test"))
    {
        Pass("Rotation live test restored the exact baseline");
    }
}

var hdrTarget = monitors
    .Select(m => (Monitor: m, Hdr: manager.GetHdrStatus(m.DeviceName)))
    .FirstOrDefault(entry => entry.Hdr?.Supported == true);
if (hdrTarget.Hdr?.Supported != true)
{
    Skip("HDR toggle live test: no active HDR-capable display reported by Windows");
}
else
{
    var original = hdrTarget.Hdr.Enabled;
    try
    {
        manager.SetHdrEnabled(hdrTarget.Monitor.DeviceName, !original);
        Thread.Sleep(1200);
        var toggled = manager.GetHdrStatus(hdrTarget.Monitor.DeviceName);
        if (toggled?.Supported == true && toggled.Enabled == !original)
        {
            Pass($"HDR toggled on {hdrTarget.Monitor.NumberedName}");
        }
        else
        {
            Fail($"HDR toggle verification failed on {hdrTarget.Monitor.NumberedName}");
        }
    }
    catch (Exception ex)
    {
        Fail($"HDR live test failed — {ex.Message}");
    }
    finally
    {
        if (RestoreBaseline("HDR live test"))
        {
            Pass("HDR live test restored the exact baseline");
        }
    }
}

Finish();

void Finish()
{
    Console.WriteLine($"\nHardware matrix: {passed} passed, {failed} failed, {skipped} skipped");
    Environment.ExitCode = failed == 0 ? 0 : 1;
}

static bool StatesMatch(DisplaySceneMonitorState? actual, DisplaySceneMonitorState expected) =>
    actual is not null &&
    actual.Width == expected.Width &&
    actual.Height == expected.Height &&
    actual.RefreshRateHz == expected.RefreshRateHz &&
    actual.PositionX == expected.PositionX &&
    actual.PositionY == expected.PositionY &&
    actual.Orientation == expected.Orientation &&
    actual.HdrEnabled == expected.HdrEnabled;
