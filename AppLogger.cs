namespace PrimaryDisplaySwap;

internal static class AppLogger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayPilot");

    private static readonly string LogPathValue = Path.Combine(LogDirectory, "log.txt");
    private static readonly string PreviousLogPath = Path.Combine(LogDirectory, "log.prev.txt");

    private static readonly object Lock = new();

    public static string LogPath => LogPathValue;
    public static string LogFolder => LogDirectory;

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPathValue, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public static void LogException(string context, Exception ex)
    {
        Log($"EXCEPTION [{context}]: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }

    /// <summary>
    /// Rotates the current run's log to log.prev.txt and starts a fresh log.
    /// Preserves the last session (including any crash trace) for diagnosis
    /// instead of discarding it on every launch.
    /// </summary>
    public static void StartNewSession()
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPathValue))
                {
                    File.Copy(LogPathValue, PreviousLogPath, overwrite: true);
                }

                File.WriteAllText(LogPathValue, string.Empty);
            }
        }
        catch
        {
            // Ignore.
        }
    }

    public static string ReadLog()
    {
        try
        {
            lock (Lock)
            {
                return File.Exists(LogPathValue) ? File.ReadAllText(LogPathValue) : string.Empty;
            }
        }
        catch (Exception ex)
        {
            return $"Could not read log: {ex.Message}";
        }
    }
}
