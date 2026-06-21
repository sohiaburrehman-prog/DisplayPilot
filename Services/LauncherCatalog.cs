namespace PrimaryDisplaySwap.Services;

/// <summary>Known game-store launchers users can pick when creating auto-swap profiles.</summary>
public static class LauncherCatalog
{
    private static readonly string[] KnownLauncherExes =
    [
        "steam.exe",
        "EpicGamesLauncher.exe",
        "GalaxyClient.exe",
        "Battle.net.exe",
        "UbisoftConnect.exe",
        "Origin.exe",
        "EADesktop.exe",
        "RiotClientServices.exe",
        "XboxApp.exe",
        "GameBar.exe",
        "GOGGalaxy.exe",
    ];

    private static readonly HashSet<string> LauncherSet = new(
        KnownLauncherExes.Select(Normalize),
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> KnownLaunchers => KnownLauncherExes;

    public static bool IsKnownLauncher(string processName) =>
        !string.IsNullOrWhiteSpace(processName) && LauncherSet.Contains(Normalize(processName));

    public static string Normalize(string processName)
    {
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }

    public static string WithExe(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
    }
}
