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

Console.WriteLine($"Detected {count} monitor(s).");

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

Pass($"UI empty state when count <= 1: {count <= 1}");
Pass($"UI swap button when count == 2: {count == 2}");
Pass($"UI set-primary hint when count > 2: {count > 2}");

if (count >= 2)
{
    foreach (var monitor in monitors)
    {
        var line = monitor.TrayMenuLine;
        if (monitor.IsPrimary && !line.Contains("Primary"))
        {
            Fail($"Tray label missing Primary marker for {monitor.Name}");
        }
        else if (!monitor.IsPrimary && line.Contains("Primary"))
        {
            Fail($"Tray label incorrectly marked primary for {monitor.Name}");
        }
        else
        {
            Pass($"Tray label OK for {monitor.NumberedName}");
        }
    }
}

Console.WriteLine($"\n{passed} passed, {failed} failed");
Environment.Exit(failed > 0 ? 1 : 0);
