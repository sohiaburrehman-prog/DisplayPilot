namespace PrimaryDisplaySwap;

static class Program
{
    private const string SingleInstanceMutexName = "Global\\DisplayPilot_SingleInstance_v1";
    private const string ShowPanelEventName = "Local\\DisplayPilot_ShowPanel_v1";

    /// <summary>Argument appended to the autostart registry entry so a boot
    /// launch starts silently in the tray instead of opening the panel.</summary>
    public const string AutostartArg = "--autostart";

    [STAThread]
    static void Main(string[] args)
    {
        using var showPanelEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowPanelEventName);

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Ask the running instance to show its control panel, then exit.
            AppLogger.Log("Second launch detected; asking running instance to show its panel.");
            showPanelEvent.Set();
            return;
        }

        var launchedAtStartup = args.Any(a =>
            string.Equals(a, AutostartArg, StringComparison.OrdinalIgnoreCase));

        AppLogger.ClearLog();
        AppLogger.Log($"Starting {AppInfo.AppName}. Exe={Environment.ProcessPath}, autostart={launchedAtStartup}");

        try
        {
            var app = new App(launchedAtStartup, showPanelEvent);
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Fatal startup error: {ex}");
            System.Windows.MessageBox.Show(
                $"{AppInfo.AppName} failed to start:\n\n{ex.Message}",
                AppInfo.AppName,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
