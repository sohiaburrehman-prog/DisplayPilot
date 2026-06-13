using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

// The project references WinForms (tray icon), whose global usings make these
// names ambiguous with System.Drawing — pin them to the WPF types.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace PrimaryDisplaySwap;

public partial class PanelWindow : Window
{
    // DWM attributes for the native Windows 11 flyout look.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtTransientWindow = 3; // acrylic, used by system flyouts

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly DisplayManager _displayManager;
    private readonly StartupService _startupService;

    private bool _suppressStartupEvent;
    private bool _swapInProgress;

    public PanelWindow(DisplayManager displayManager, StartupService startupService)
    {
        _displayManager = displayManager;
        _startupService = startupService;

        InitializeComponent();

        Title = AppInfo.AppName;
        TitleText.Text = AppInfo.AppName;
        VersionText.Text = $"v{AppInfo.AppVersion}";

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HideToTray();
            }
        };

        // Flyout behaviour: clicking anywhere else dismisses the panel.
        // Guarded during swaps — applying a display config steals focus.
        Deactivated += (_, _) =>
        {
            if (IsVisible && !_swapInProgress)
            {
                HideToTray();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        var dark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var corner = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

        // Acrylic system backdrop (Windows 11 22H2+). On older builds, fall
        // back to a fully opaque surface so the window doesn't render black
        // behind the semi-transparent theme brushes.
        var acrylicApplied = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            var backdrop = DwmsbtTransientWindow;
            acrylicApplied = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }

        if (!acrylicApplied)
        {
            RootSurface.Background = (Brush)FindResource("FlyoutOpaqueBrush");
            AppLogger.Log("Acrylic backdrop unavailable; using opaque surface.");
        }
    }

    /// <summary>Positions the panel just above the tray (bottom-right of the
    /// primary work area), shows it, and plays the entrance animation.</summary>
    public void ShowNearTray()
    {
        var wasVisible = IsVisible;

        // Measure first so ActualWidth/Height are valid for positioning.
        if (!wasVisible)
        {
            RootSurface.Opacity = 0;
            Show();
        }

        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;

        Activate();

        if (!wasVisible)
        {
            PlayEntranceAnimation();
        }
    }

    private void PlayEntranceAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        RootSurface.BeginAnimation(OpacityProperty, fade);

        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        EntranceShift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void HideToTray()
    {
        Hide();
        RestoreHintWindow.ShowBriefHint();
    }

    public void RefreshMonitors()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshMonitors);
            return;
        }

        _suppressStartupEvent = true;
        StartupToggle.IsChecked = _startupService.IsEnabled;
        _suppressStartupEvent = false;

        MonitorList.Children.Clear();

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayManager.GetMonitors();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
            EmptyState.Text = "Could not read displays.";
            EmptyState.Visibility = Visibility.Visible;
            SwapButton.Visibility = Visibility.Collapsed;
            return;
        }

        if (monitors.Count <= 1)
        {
            EmptyState.Text = monitors.Count == 0
                ? "No displays detected."
                : "Only one monitor connected. Connect another display to swap.";
            EmptyState.Visibility = Visibility.Visible;
            SwapButton.Visibility = Visibility.Collapsed;
            MapHost.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        SwapButton.Visibility = monitors.Count == 2 ? Visibility.Visible : Visibility.Collapsed;

        BuildArrangementMap(monitors);
        MapHost.Visibility = Visibility.Visible;

        foreach (var monitor in monitors)
        {
            MonitorList.Children.Add(BuildMonitorCard(monitor));
        }

        if (IsVisible)
        {
            PlayCardStagger();
        }
    }

    /// <summary>Draws the physical monitor arrangement to scale, like the
    /// Windows display-settings diagram. Clicking a monitor makes it primary.</summary>
    private void BuildArrangementMap(IReadOnlyList<MonitorInfo> monitors)
    {
        ArrangementCanvas.Children.Clear();

        const double mapHeight = 104;
        var mapWidth = Math.Max(ArrangementCanvas.ActualWidth, 320.0);
        const double pad = 4;

        double minX = monitors.Min(m => (double)m.PositionX);
        double minY = monitors.Min(m => (double)m.PositionY);
        double maxX = monitors.Max(m => (double)m.PositionX + m.Width);
        double maxY = monitors.Max(m => (double)m.PositionY + m.Height);

        var scale = Math.Min(
            (mapWidth - pad * 2) / Math.Max(maxX - minX, 1),
            (mapHeight - pad * 2) / Math.Max(maxY - minY, 1));

        var offsetX = (mapWidth - (maxX - minX) * scale) / 2;
        var offsetY = (mapHeight - (maxY - minY) * scale) / 2;

        foreach (var monitor in monitors)
        {
            var w = monitor.Width * scale - 4;
            var h = monitor.Height * scale - 4;

            var screen = new Border
            {
                Width = Math.Max(w, 24),
                Height = Math.Max(h, 16),
                CornerRadius = new CornerRadius(5),
                Background = monitor.IsPrimary
                    ? (Brush)FindResource("ScreenGradientBrush")
                    : (Brush)FindResource("ScreenIdleBrush"),
                BorderBrush = monitor.IsPrimary
                    ? (Brush)FindResource("AccentHoverBrush")
                    : (Brush)FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1),
                Cursor = monitor.IsPrimary ? Cursors.Arrow : Cursors.Hand,
                ToolTip = $"{monitor.Name}\n{monitor.Width} × {monitor.Height}" +
                          (monitor.IsPrimary ? "  ·  primary" : "  ·  click to make primary"),
            };

            if (monitor.IsPrimary)
            {
                screen.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x4F, 0x8D, 0xFF),
                    BlurRadius = 16,
                    ShadowDepth = 0,
                    Opacity = 0.45,
                };
            }

            var label = new TextBlock
            {
                Text = (monitor.Index + 1).ToString(),
                FontFamily = (FontFamily)FindResource("UiFont"),
                FontSize = Math.Max(Math.Min(h * 0.38, 22), 11),
                FontWeight = FontWeights.Bold,
                Foreground = monitor.IsPrimary
                    ? Brushes.White
                    : (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            screen.Child = label;

            if (!monitor.IsPrimary)
            {
                var index = monitor.Index;
                var name = monitor.Name;
                screen.MouseLeftButtonUp += async (_, _) => await SetPrimaryAsync(index, name);
                screen.MouseEnter += (s, _) => ((Border)s).Background = (Brush)FindResource("CardHoverBrush");
                screen.MouseLeave += (s, _) => ((Border)s).Background = (Brush)FindResource("ScreenIdleBrush");
            }

            Canvas.SetLeft(screen, offsetX + (monitor.PositionX - minX) * scale + 2);
            Canvas.SetTop(screen, offsetY + (monitor.PositionY - minY) * scale + 2);
            ArrangementCanvas.Children.Add(screen);
        }
    }

    /// <summary>Fades the monitor cards in one after another.</summary>
    private void PlayCardStagger()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var delay = 0;

        foreach (UIElement child in MonitorList.Children)
        {
            child.Opacity = 0;
            var shift = new TranslateTransform(0, 10);
            child.RenderTransform = shift;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = ease,
            };
            var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = ease,
            };

            child.BeginAnimation(OpacityProperty, fade);
            shift.BeginAnimation(TranslateTransform.YProperty, slide);
            delay += 55;
        }
    }

    private UIElement BuildMonitorCard(MonitorInfo monitor)
    {
        var card = new Button
        {
            Style = (Style)FindResource("MonitorCard"),
            Height = 64,
            Margin = new Thickness(0, 0, 0, 8),
            IsEnabled = !monitor.IsPrimary && !_swapInProgress,
            ToolTip = monitor.IsPrimary
                ? $"{monitor.Name} is the primary display"
                : $"Make {monitor.Name} the primary display",
        };

        if (monitor.IsPrimary)
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0x4F, 0x8D, 0xFF));
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Accent bar marking the current primary.
        if (monitor.IsPrimary)
        {
            var bar = new Rectangle
            {
                Width = 3,
                Height = 30,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = (Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 11, 0),
            };
            Grid.SetColumn(bar, 0);
            grid.Children.Add(bar);
        }

        // Vector monitor glyph — drawn at the monitor's real aspect ratio.
        var glyph = BuildMonitorGlyph(monitor);
        Grid.SetColumn(glyph, 1);
        grid.Children.Add(glyph);

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0),
        };
        textStack.Children.Add(new TextBlock
        {
            Text = monitor.Name,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = monitor.RefreshRateHz > 0
                ? $"{monitor.Width} × {monitor.Height} · {monitor.RefreshRateHz} Hz"
                : $"{monitor.Width} × {monitor.Height}",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);

        if (monitor.IsPrimary)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(48, 0x4F, 0x8D, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0x4F, 0x8D, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "PRIMARY",
                    FontFamily = (FontFamily)FindResource("UiFont"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AccentHoverBrush"),
                },
            };
            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);
        }

        card.Content = grid;

        if (!monitor.IsPrimary)
        {
            var index = monitor.Index;
            var name = monitor.Name;
            card.Click += async (_, _) => await SetPrimaryAsync(index, name);
        }

        return card;
    }

    private UIElement BuildMonitorGlyph(MonitorInfo monitor)
    {
        var accent = monitor.IsPrimary
            ? new SolidColorBrush(Color.FromRgb(0x74, 0xA6, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x8C));

        // Real aspect ratio (ultrawides come out wide, 16:9 stays compact).
        var ratio = monitor.Width > 0 ? (double)monitor.Height / monitor.Width : 0.58;
        var screenW = 36.0;
        var screenH = Math.Clamp(screenW * ratio, 13, 26);

        var canvas = new Canvas
        {
            Width = screenW + 2,
            Height = screenH + 7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var screen = new Border
        {
            Width = screenW,
            Height = screenH,
            CornerRadius = new CornerRadius(3.5),
            BorderBrush = accent,
            BorderThickness = new Thickness(1.6),
            Background = monitor.IsPrimary
                ? (Brush)FindResource("ScreenGradientBrush")
                : (Brush)FindResource("ScreenIdleBrush"),
        };
        Canvas.SetLeft(screen, 1);
        Canvas.SetTop(screen, 0);
        canvas.Children.Add(screen);

        var stand = new Rectangle
        {
            Width = 12,
            Height = 2.6,
            RadiusX = 1.3,
            RadiusY = 1.3,
            Fill = accent,
        };
        Canvas.SetLeft(stand, (screenW + 2 - 12) / 2);
        Canvas.SetTop(stand, screenH + 3);
        canvas.Children.Add(stand);

        return canvas;
    }

    private async void Swap_Click(object sender, RoutedEventArgs e)
    {
        if (_swapInProgress)
        {
            return;
        }

        try
        {
            _swapInProgress = true;
            SetBusy(true, "Swapping…");

            var newPrimary = await Task.Run(() => _displayManager.SwapPrimaryBetweenTwoMonitors());

            RefreshMonitors();
            ShowStatus($"Swapped — {newPrimary.Name} is now primary.", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
        finally
        {
            _swapInProgress = false;
            SetBusy(false);
        }
    }

    private async Task SetPrimaryAsync(int monitorIndex, string monitorName)
    {
        if (_swapInProgress)
        {
            return;
        }

        try
        {
            _swapInProgress = true;
            SetBusy(true, $"Making {monitorName} primary…");

            var newPrimary = await Task.Run(() => _displayManager.SetPrimaryMonitor(monitorIndex));

            RefreshMonitors();
            ShowStatus($"Primary set to {newPrimary.Name}.", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
        finally
        {
            _swapInProgress = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        SwapButton.IsEnabled = !busy;
        MonitorList.IsEnabled = !busy;
        MapHost.IsEnabled = !busy;

        if (busy)
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            SwapIconRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            SwapIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        if (busy && message != null)
        {
            ShowStatus(message, success: null);
        }
    }

    private void ShowStatus(string message, bool? success)
    {
        StatusText.Text = message;
        StatusText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        StatusText.Foreground = success switch
        {
            true => (Brush)FindResource("SuccessBrush"),
            false => (Brush)FindResource("ErrorBrush"),
            null => (Brush)FindResource("TextMutedBrush"),
        };

        if (success == false)
        {
            AppLogger.Log($"Panel error: {message}");
        }
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressStartupEvent)
        {
            return;
        }

        try
        {
            _startupService.SetEnabled(StartupToggle.IsChecked == true);
            ShowStatus(
                StartupToggle.IsChecked == true
                    ? "Will start hidden in the tray when Windows starts."
                    : "Removed from Windows startup.",
                success: null);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
            _suppressStartupEvent = true;
            StartupToggle.IsChecked = _startupService.IsEnabled;
            _suppressStartupEvent = false;
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the window hides it; the app lives in the tray until Exit.
        e.Cancel = true;
        HideToTray();
    }
}
