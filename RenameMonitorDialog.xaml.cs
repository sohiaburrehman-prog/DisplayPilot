using System.Windows;

using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap;

public partial class RenameMonitorDialog : Window
{
    public string? ResultNickname { get; private set; }

    public string MonitorName => ResultNickname ?? string.Empty;

    public RenameMonitorDialog(MonitorInfo monitor, string? currentNickname)
        : this(currentNickname ?? string.Empty, "Rename monitor", "Nickname", $"Hardware: {monitor.Name}",
            "Leave blank to use the hardware name.")
    {
    }

    public RenameMonitorDialog(
        string currentName,
        string title,
        string fieldLabel,
        string? subtitle = null,
        string? hint = null)
    {
        InitializeComponent();

        Title = title;
        HardwareNameText.Text = subtitle ?? string.Empty;
        HardwareNameText.Visibility = string.IsNullOrWhiteSpace(subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
        FieldLabelText.Text = fieldLabel;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintText.Text = hint;
        }

        NicknameBox.Text = currentName;
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
