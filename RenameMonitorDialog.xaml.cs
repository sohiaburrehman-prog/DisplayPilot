using System.Windows;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap;

public partial class RenameMonitorDialog : Window
{
    public string? ResultNickname { get; private set; }

    public RenameMonitorDialog(MonitorInfo monitor, string? currentNickname)
    {
        InitializeComponent();

        HardwareNameText.Text = $"Hardware: {monitor.Name}";
        NicknameBox.Text = currentNickname ?? string.Empty;
        NicknameBox.SelectAll();
        NicknameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultNickname = NicknameBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
