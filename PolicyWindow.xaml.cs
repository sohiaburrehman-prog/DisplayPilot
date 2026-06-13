using System.Windows;

namespace PrimaryDisplaySwap;

public partial class PolicyWindow : Window
{
    public PolicyWindow(string title, string body, string? subtitle = null)
    {
        InitializeComponent();
        Title = $"{AppInfo.AppName} — {title}";
        TitleText.Text = title;
        SubtitleText.Text = subtitle ?? $"{AppInfo.AppName} v{AppInfo.AppVersion}";
        BodyText.Text = body;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
