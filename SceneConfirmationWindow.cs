using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using SystemFonts = System.Windows.SystemFonts;

namespace PrimaryDisplaySwap;

/// <summary>Windows-style keep/revert countdown after applying a display scene.</summary>
internal sealed class SceneConfirmationWindow : Window
{
    private readonly TextBlock _countdownText;
    private readonly DispatcherTimer _timer;
    private int _secondsRemaining;

    private SceneConfirmationWindow(string sceneName, int timeoutSeconds)
    {
        _secondsRemaining = Math.Clamp(timeoutSeconds, 5, 120);
        Title = "Keep display changes?";
        Width = 430;
        Height = 235;
        MinWidth = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        Topmost = true;
        Background = FindBrush("FlyoutOpaqueBrush", Color.FromRgb(28, 31, 38));
        FontFamily = SystemFonts.MessageFontFamily;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = $"Scene “{sceneName}” was applied.",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush", Colors.White),
        };
        root.Children.Add(heading);

        var explanation = new TextBlock
        {
            Text = "Can you see and use every display? If you do nothing, DisplayPilot will restore the previous scene automatically.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = FindBrush("TextSecondaryBrush", Color.FromRgb(210, 214, 222)),
        };
        Grid.SetRow(explanation, 1);
        root.Children.Add(explanation);

        _countdownText = new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("AccentBrush", Color.FromRgb(94, 170, 255)),
        };
        Grid.SetRow(_countdownText, 2);
        root.Children.Add(_countdownText);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var revert = new Button
        {
            Content = "Revert now",
            Width = 110,
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };
        revert.Click += (_, _) => Close();
        var keep = new Button
        {
            Content = "Keep changes",
            Width = 120,
            Height = 36,
            IsDefault = true,
        };
        keep.Click += (_, _) =>
        {
            KeepChanges = true;
            Close();
        };
        buttons.Children.Add(revert);
        buttons.Children.Add(keep);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _secondsRemaining--;
            UpdateCountdown();
            if (_secondsRemaining <= 0)
            {
                Close();
            }
        };
        Loaded += (_, _) =>
        {
            UpdateCountdown();
            _timer.Start();
            Activate();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public bool KeepChanges { get; private set; }

    public static bool Confirm(Window? owner, string sceneName, int timeoutSeconds = 15)
    {
        var window = new SceneConfirmationWindow(sceneName, timeoutSeconds);
        if (owner?.IsVisible == true)
        {
            window.Owner = owner;
        }
        window.ShowDialog();
        return window.KeepChanges;
    }

    private void UpdateCountdown() =>
        _countdownText.Text = $"Reverting in {Math.Max(0, _secondsRemaining)} second{(_secondsRemaining == 1 ? string.Empty : "s")}…";

    private static System.Windows.Media.Brush FindBrush(string key, System.Windows.Media.Color fallback) =>
        Application.Current?.TryFindResource(key) as System.Windows.Media.Brush
        ?? new System.Windows.Media.SolidColorBrush(fallback);
}
