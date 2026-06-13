namespace PrimaryDisplaySwap;

internal static class AppLogger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayPilot");

    private static readonly string LogPath = Path.Combine(LogDirectory, "log.txt");

    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public static void ClearLog()
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(LogPath, string.Empty);
            }
        }
        catch
        {
            // Ignore.
        }
    }
}
