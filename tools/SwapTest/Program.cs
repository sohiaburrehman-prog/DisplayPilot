using PrimaryDisplaySwap.Services;

var manager = new DisplayManager();

void Dump(string label)
{
    Console.WriteLine($"--- {label} ---");
    foreach (var m in manager.GetMonitors())
    {
        Console.WriteLine($"  [{m.Index}] {m.Name} {m.DeviceName} {m.Width}x{m.Height} at ({m.PositionX},{m.PositionY}) primary={m.IsPrimary}");
    }
}

Dump("before");

if (args.Contains("--modes"))
{
    // Dry run: enumerate available resolution/refresh modes per monitor.
    foreach (var m in manager.GetMonitors())
    {
        var current = manager.GetCurrentMode(m.DeviceName);
        Console.WriteLine($"\n{m.NumberedName} ({m.DeviceName}) — current: {current?.Label ?? "?"}");
        foreach (var mode in manager.GetAvailableModes(m.DeviceName))
        {
            Console.WriteLine($"    {mode.Label}");
        }
    }

    return;
}

if (args.Contains("--once"))
{
    var p = manager.SwapPrimaryBetweenTwoMonitors();
    Console.WriteLine($"Swapped once. New primary: {p.Name}");
    Thread.Sleep(2000);
    Dump("after");
    return;
}

var newPrimary = manager.SwapPrimaryBetweenTwoMonitors();
Console.WriteLine($"Swapped. New primary: {newPrimary.Name}");
Thread.Sleep(2000);
Dump("after swap");

var restored = manager.SwapPrimaryBetweenTwoMonitors();
Console.WriteLine($"Swapped back. New primary: {restored.Name}");
Thread.Sleep(2000);
Dump("after swap back");
