using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace PrimaryDisplaySwap;

/// <summary>Brief on-screen reminder when the flyout is hidden to tray.</summary>
internal sealed class RestoreHintWindow : Window
{
    private readonly DispatcherTimer _closeTimer;
    private int _ticksRemaining = 30;

    private RestoreHintWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 360;
        Height = 56;
        Opacity = 0.95;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        var surface = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = "Hidden — press Ctrl+Shift+M to restore",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xEB, 0xF5)),
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };

        Content = surface;

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _closeTimer.Tick += (_, _) =>
        {
            _ticksRemaining--;
            if (_ticksRemaining <= 10)
            {
                Opacity = Math.Max(0, _ticksRemaining / 10d * 0.95);
            }

            if (_ticksRemaining <= 0)
            {
                _closeTimer.Stop();
                Close();
            }
        };
    }

    public static void ShowBriefHint()
    {
        var window = new RestoreHintWindow();
        var area = SystemParameters.WorkArea;
        window.Left = area.Right - window.Width - 16;
        window.Top = area.Bottom - window.Height - 16;
        window.Show();
        window._closeTimer.Start();
    }
}
