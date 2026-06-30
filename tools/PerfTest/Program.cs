using System;
using System.Diagnostics;
using PrimaryDisplaySwap.Models;

class Program {
    static void Main() {
        var p = new AppProfile { ProcessName = "MyGame.exe", ResolvedTargetProcessName = "GameClient.exe" };
        var sw = Stopwatch.StartNew();
        int count = 0;
        for (int i = 0; i < 1_000_000; i++) {
            if (p.NormalizedProcessName != null) count++;
            if (p.NormalizedResolvedTarget != null) count++;
        }
        Console.WriteLine($"Computed properties: {sw.ElapsedMilliseconds} ms");
    }
}
