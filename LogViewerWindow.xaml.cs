using System.Diagnostics;
using System.Windows;

using Clipboard = System.Windows.Clipboard;

namespace PrimaryDisplaySwap;

public partial class LogViewerWindow : Window
{
    public LogViewerWindow()
    {
        InitializeComponent();
        PathText.Text = AppLogger.LogPath;
        RefreshLog();
    }

    public void RefreshLog()
    {
        var content = AppLogger.ReadLog();
        LogText.Text = string.IsNullOrWhiteSpace(content)
            ? "The log is empty for this session."
            : content;

        // Scroll to the newest entries.
        Dispatcher.BeginInvoke(() =>
        {
            LogText.CaretIndex = LogText.Text.Length;
            LogScroll.ScrollToEnd();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLog();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogText.Text);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Copy log failed: {ex.Message}");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var safePath = AppLogger.LogPath.Replace("\"", "");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{safePath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Open log folder failed: {ex.Message}");
            try
            {
                Process.Start(new ProcessStartInfo(AppLogger.LogFolder) { UseShellExecute = true });
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
