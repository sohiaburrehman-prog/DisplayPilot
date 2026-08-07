using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Native;
using PrimaryDisplaySwap.Services;

// The project references WinForms (tray icon), whose global usings make these
// names ambiguous with System.Drawing — pin them to the WPF types.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Rectangle = System.Windows.Shapes.Rectangle;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace PrimaryDisplaySwap;

public partial class PanelWindow : Window
{
    public event EventHandler? SettingsRequested;
    public event EventHandler? ProfilesRequested;
    public event EventHandler? ViewLogRequested;

    private string _updateReleaseUrl = string.Empty;
    private string _updateTag = string.Empty;
    private string _whatsNewVersion = string.Empty;
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
    private readonly SettingsService _settings;

    private bool _suppressStartupEvent;
    private bool _suppressHdrEvent;
    private string? _undoDeviceName;
    private System.Windows.Threading.DispatcherTimer? _toastTimer;
    private bool _swapInProgress;
    private bool _loadingModeEditors;
    private readonly List<Action> _pendingModeLoads = [];
    private IReadOnlyList<MonitorInfo> _lastMapMonitors = Array.Empty<MonitorInfo>();

    private const double ArrangementMapHeight = 104;

    public PanelWindow(DisplayManager displayManager, StartupService startupService, SettingsService settings)
    {
        _displayManager = displayManager;
        _startupService = startupService;
        _settings = settings;

        InitializeComponent();

        Title = AppInfo.AppName;
        TitleText.Text = AppInfo.AppName;
        VersionText.Text = $"v{AppInfo.AppVersion}";
        RefreshHotkeyHints();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HideToTray();
            }
        };


        MapScroll.SizeChanged += (_, _) => RebuildArrangementMapIfNeeded();
        MapScroll.PreviewMouseWheel += MapScroll_PreviewMouseWheel;

        // Flyout behaviour: clicking elsewhere dismisses the panel. Defer one
        // frame so opening the tray context menu does not instantly hide the flyout.
        Deactivated += (_, _) =>
        {
            if (!IsVisible || _swapInProgress || TrayUiState.TrayMenuOpen)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (!IsVisible || _swapInProgress || TrayUiState.TrayMenuOpen || IsActive)
                {
                    return;
                }

                HideToTray();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Match the DWM backdrop tint to the active theme.
        var dark = ThemeManager.IsLight ? 0 : 1;
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
    /// current work area), shows it, and plays the entrance animation.</summary>
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
        PositionInWorkArea();

        Activate();

        if (!wasVisible)
        {
            PlayEntranceAnimation();
        }
    }

    /// <summary>
    /// Recomputes flyout placement from live Win32 work-area bounds (and clamps
    /// on-screen). Call on every show and after DisplaySettingsChanged — WPF's
    /// <see cref="SystemParameters.WorkArea"/> stays stale across resolution changes.
    /// </summary>
    public void PositionInWorkArea()
    {
        UpdateLayout();

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (!WindowInterop.GetCursorPos(out var cursor))
        {
            cursor = default; // (0,0) → primary via MonitorDefaultToNearest/Primary
        }

        if (hwnd == IntPtr.Zero ||
            !WindowInterop.TryGetWorkAreaPixels(cursor, out var work, out var dpiX, out var dpiY))
        {
            // Pre-hwnd / Win32 failure: fall back to (possibly stale) WPF work area.
            var area = SystemParameters.WorkArea;
            Left = Clamp(area.Right - width - 16, area.Left, area.Right - width);
            Top = Clamp(area.Bottom - height - 16, area.Top, area.Bottom - height);
            return;
        }

        // Place with SetWindowPos in physical pixels so PerMonitorV2 DPI and
        // multi-monitor origins stay correct after 4K→1440p (etc.) changes.
        var widthPx = Math.Max(1, (int)Math.Ceiling(width * dpiX / 96.0));
        var heightPx = Math.Max(1, (int)Math.Ceiling(height * dpiY / 96.0));
        var marginX = (int)Math.Round(16 * dpiX / 96.0);
        var marginY = (int)Math.Round(16 * dpiY / 96.0);

        var x = work.Right - widthPx - marginX;
        var y = work.Bottom - heightPx - marginY;
        x = Clamp(x, work.Left, work.Right - widthPx);
        y = Clamp(y, work.Top, work.Bottom - heightPx);

        WindowInterop.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOZORDER | WindowInterop.SWP_NOACTIVATE);
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);

    private static int Clamp(int value, int min, int max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);

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

        RefreshProfilesSummary();

        // Rows are about to be discarded — drop references to the open one so
        // CollapseOpenRow() can never touch a detached element.
        _openDetail = null;
        _openChevron = null;

        MonitorList.Children.Clear();
        _pendingModeLoads.Clear();

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayManager.GetMonitors();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
            EmptyStateTitle.Text = "Could not read displays";
            EmptyState.Text = "Try reopening the panel or check the log file.";
            EmptyStateHost.Visibility = Visibility.Visible;
            MapHost.Visibility = Visibility.Collapsed;
            MapHint.Visibility = Visibility.Collapsed;
            _lastMapMonitors = Array.Empty<MonitorInfo>();
            return;
        }

        if (monitors.Count == 0)
        {
            EmptyStateTitle.Text = "No displays detected";
            EmptyState.Text = "Windows did not report any active displays.";
            EmptyStateHost.Visibility = Visibility.Visible;
            MapHost.Visibility = Visibility.Collapsed;
            MapHint.Visibility = Visibility.Collapsed;
            _lastMapMonitors = Array.Empty<MonitorInfo>();
            return;
        }

        EmptyStateHost.Visibility = Visibility.Collapsed;

        // The map is the primary control for choosing a display; with a single
        // monitor there is nothing to arrange, so it is hidden.
        if (monitors.Count > 1)
        {
            BuildArrangementMap(monitors);
            _lastMapMonitors = monitors;
            MapHost.Visibility = Visibility.Visible;
            MapHint.Visibility = Visibility.Visible;
        }
        else
        {
            MapHost.Visibility = Visibility.Collapsed;
            MapHint.Visibility = Visibility.Collapsed;
            _lastMapMonitors = Array.Empty<MonitorInfo>();
        }

        // One list: each row carries the mode controls for its own display.
        foreach (var monitor in monitors)
        {
            MonitorList.Children.Add(BuildDisplayRow(monitor, monitors.Count > 1));
        }

        LoadPendingModeEditors();

        if (IsVisible)
        {
            PlayCardStagger();
        }
    }

    /// <summary>
    /// Loads the resolution/refresh/HDR options for every row. Mode enumeration
    /// hits the display driver, so it runs once per refresh rather than on
    /// every expand.
    /// </summary>
    private void LoadPendingModeEditors()
    {
        if (_pendingModeLoads.Count == 0 || _loadingModeEditors)
        {
            return;
        }

        _loadingModeEditors = true;
        try
        {
            var loads = _pendingModeLoads.ToArray();
            _pendingModeLoads.Clear();

            foreach (var load in loads)
            {
                load();
            }
        }
        finally
        {
            _loadingModeEditors = false;
        }
    }

    /// <summary>Draws the physical monitor arrangement to scale, like the
    /// Windows display-settings diagram. Clicking a monitor makes it primary.</summary>
    private void BuildArrangementMap(IReadOnlyList<MonitorInfo> monitors)
    {
        ArrangementCanvas.Children.Clear();

        var viewportWidth = MapScroll.ActualWidth > 0
            ? MapScroll.ActualWidth
            : Math.Max(ArrangementCanvas.ActualWidth, 320.0);
        var layout = ArrangementMapLayout.Compute(monitors, viewportWidth, ArrangementMapHeight);
        var isTwoMonitorSwap = monitors.Count == 2;

        ArrangementCanvas.Width = layout.ContentWidth;
        ArrangementCanvas.Height = isTwoMonitorSwap
            ? layout.ContentHeight + 18
            : layout.ContentHeight;
        MapScroll.HorizontalScrollBarVisibility = layout.NeedsHorizontalScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        if (!layout.NeedsHorizontalScroll)
        {
            MapScroll.ScrollToHorizontalOffset(0);
        }

        foreach (var tile in layout.Tiles)
        {
            var monitor = tile.Monitor;
            var screenW = tile.Width;
            var screenH = tile.Height;

            var screen = new Border
            {
                Width = screenW,
                Height = screenH,
                CornerRadius = new CornerRadius(5),
                Background = monitor.IsPrimary
                    ? (Brush)FindResource("ScreenGradientBrush")
                    : (Brush)FindResource("ScreenIdleBrush"),
                BorderBrush = monitor.IsPrimary
                    ? (Brush)FindResource("AccentHoverBrush")
                    : (Brush)FindResource("HairlineBrush"),
                BorderThickness = new Thickness(monitor.IsPrimary ? 1.5 : 1),
                Cursor = monitor.IsPrimary ? Cursors.Arrow : Cursors.Hand,
                ToolTip = MonitorDisplayHelper.GetMapTooltip(monitor, _settings.Current),
                Tag = monitor,
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

            var labelStack = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            labelStack.Children.Add(new TextBlock
            {
                Text = (monitor.Index + 1).ToString(),
                FontFamily = (FontFamily)FindResource("UiFont"),
                FontSize = Math.Max(Math.Min(screenH * 0.38, 22), 11),
                FontWeight = FontWeights.Bold,
                Foreground = monitor.IsPrimary
                    ? Brushes.White
                    : (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });

            if (screenH >= 28)
            {
                labelStack.Children.Add(new TextBlock
                {
                    Text = ShortName(MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current), 10),
                    FontFamily = (FontFamily)FindResource("UiFont"),
                    FontSize = 8.5,
                    Foreground = monitor.IsPrimary
                        ? Brushes.White
                        : (Brush)FindResource("TextMutedBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = screenW - 6,
                    Margin = new Thickness(0, 1, 0, 0),
                    Opacity = 0.9,
                });
            }

            screen.Child = labelStack;

            var deviceName = monitor.DeviceName;
            var displayName = MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current);
            var idleBrush = monitor.IsPrimary
                ? (Brush)FindResource("ScreenGradientBrush")
                : (Brush)FindResource("ScreenIdleBrush");

            if (!monitor.IsPrimary)
            {
                WireSetPrimaryActivation(screen, deviceName, displayName);
            }

            screen.MouseEnter += (s, _) =>
            {
                var border = (Border)s;
                border.Background = (Brush)FindResource("CardHoverBrush");
                if (!monitor.IsPrimary)
                {
                    border.BorderBrush = (Brush)FindResource("AccentBrush");
                }
            };
            screen.MouseLeave += (s, _) =>
            {
                var border = (Border)s;
                border.Background = idleBrush;
                border.BorderBrush = monitor.IsPrimary
                    ? (Brush)FindResource("AccentHoverBrush")
                    : (Brush)FindResource("HairlineBrush");
            };

            Canvas.SetLeft(screen, tile.Left);
            Canvas.SetTop(screen, tile.Top);
            ArrangementCanvas.Children.Add(screen);
        }

        if (isTwoMonitorSwap)
        {
            var hint = new TextBlock
            {
                Text = "Click a display to make it primary",
                FontFamily = (FontFamily)FindResource("UiFont"),
                FontSize = 9.5,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            Canvas.SetLeft(hint, 0);
            Canvas.SetTop(hint, layout.ContentHeight + 2);
            ArrangementCanvas.Children.Add(hint);
        }
    }

    /// <summary>Quick scale-and-fade "pop" on the current primary tile after a
    /// change, drawing the eye to the display that just became primary.</summary>
    private void PulsePrimaryTile()
    {
        var primary = ArrangementCanvas.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag is MonitorInfo m && m.IsPrimary);
        if (primary is null)
        {
            return;
        }

        var scale = new ScaleTransform(0.82, 0.82);
        primary.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        primary.RenderTransform = scale;

        var pop = new DoubleAnimation(0.82, 1.0, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void RebuildArrangementMapIfNeeded()
    {
        if (_lastMapMonitors.Count <= 1 || MapHost.Visibility != Visibility.Visible)
        {
            return;
        }

        if (MapScroll.ActualWidth <= 0)
        {
            return;
        }

        BuildArrangementMap(_lastMapMonitors);
    }

    /// <summary>Wheel over the map pans horizontally so off-screen displays stay reachable.</summary>
    private void MapScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (MapScroll.ScrollableWidth <= 0)
        {
            return;
        }

        var nextOffset = MapScroll.HorizontalOffset - e.Delta;
        MapScroll.ScrollToHorizontalOffset(Math.Clamp(nextOffset, 0, MapScroll.ScrollableWidth));
        e.Handled = true;
    }

    private void ShowRenameDialog(MonitorInfo monitor)
    {
        _settings.Current.MonitorNicknames.TryGetValue(monitor.DeviceName, out var current);
        var dialog = new RenameMonitorDialog(monitor, current)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        MonitorDisplayHelper.SetNickname(_settings, monitor, dialog.ResultNickname);
        RefreshMonitors();
        ShowStatus(
            string.IsNullOrWhiteSpace(dialog.ResultNickname)
                ? $"Using hardware name for {monitor.Name}."
                : $"Renamed to \"{dialog.ResultNickname}\".",
            success: true);
    }

    private UIElement CreateRenameLink(MonitorInfo monitor)
    {
        var link = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var hyperlink = new System.Windows.Documents.Hyperlink
        {
            Foreground = (Brush)FindResource("AccentHoverBrush"),
            TextDecorations = null,
            Focusable = false,
        };
        hyperlink.Inlines.Add("Rename");
        hyperlink.Click += (_, e) =>
        {
            e.Handled = true;
            ShowRenameDialog(monitor);
        };
        link.Inlines.Add(hyperlink);
        return link;
    }

    private static string ShortName(string name, int maxLen = 18)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length <= maxLen)
        {
            return name;
        }

        return name[..(maxLen - 1)] + "…";
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

    /// <summary>
    /// Per-monitor resolution + refresh card for the Advanced tab. Modes load
    /// lazily when that tab is first selected to keep refreshes fast.
    /// </summary>
    /// <summary>
    /// One display row: a fixed-height header that sets primary on click, and a
    /// collapsible detail area holding the resolution, refresh and HDR controls
    /// for that same display. Replaces the old split across two tabs.
    /// </summary>
    private UIElement BuildDisplayRow(MonitorInfo monitor, bool canSetPrimary)
    {
        var deviceName = monitor.DeviceName;

        var resolutionCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            ItemTemplate = (DataTemplate)FindResource("ResolutionItemTemplate"),
            Margin = new Thickness(0, 0, 4, 0),
            MinWidth = 100,
            IsEnabled = false,
        };
        // Refresh-only labels ("240 Hz"): the resolution is already shown in the
        // first picker, so repeating it here just truncated.
        var refreshCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            ItemTemplate = (DataTemplate)FindResource("RefreshItemTemplate"),
            Width = 84,
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = false,
        };
        // Apply is created here but only shown once a change is staged — a
        // full-weight accent button sitting lit with nothing to apply was the
        // brightest no-op in the panel.
        var applyButton = new Button
        {
            Style = (Style)FindResource("AccentMiniButton"),
            Content = "Apply",
            MinWidth = 58,
            Padding = new Thickness(10, 0, 10, 0),
            IsEnabled = false,
        };

        TextBlock FieldLabel(string text) => new()
        {
            Text = text,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 5),
        };

        var resolutionStack = new StackPanel();
        resolutionStack.Children.Add(FieldLabel("Resolution"));
        resolutionCombo.Margin = new Thickness(0);
        resolutionStack.Children.Add(resolutionCombo);

        var refreshStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0), Width = 104 };
        refreshStack.Children.Add(FieldLabel("Refresh"));
        refreshCombo.Margin = new Thickness(0);
        refreshCombo.Width = double.NaN;
        refreshStack.Children.Add(refreshCombo);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(resolutionStack, 0);
        Grid.SetColumn(refreshStack, 1);
        grid.Children.Add(resolutionStack);
        grid.Children.Add(refreshStack);

        // Post-apply confirmation strip: "Applied · reverting in 12s" + Keep.
        // Mirrors what Windows itself does for display-mode changes, so a bad
        // mode can't leave the screen unusable.
        var confirmRow = new RevertConfirmRow(this);

        // HDR is a device state like "Start with Windows", so it uses the same
        // switch rather than a checkbox, with a sub-line for bit depth or the
        // reason it is unavailable at the current mode.
        var hdrSwitch = new ToggleButton
        {
            Style = (Style)FindResource("ToggleSwitch"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hdrTitle = new TextBlock
        {
            Text = "HDR",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        var hdrSub = new TextBlock
        {
            Text = "Off",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var hdrTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hdrTextStack.Children.Add(hdrTitle);
        hdrTextStack.Children.Add(hdrSub);

        var hdrRow = new Grid { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(hdrTextStack, 0);
        Grid.SetColumn(hdrSwitch, 1);
        hdrRow.Children.Add(hdrTextStack);
        hdrRow.Children.Add(hdrSwitch);

        var loaded = false;
        var suppressComboEvents = false;
        IReadOnlyList<DisplayMode> modes = Array.Empty<DisplayMode>();

        void PopulateRefreshRates()
        {
            if (resolutionCombo.SelectedItem is not DisplayMode selectedRes)
            {
                return;
            }

            var rates = modes
                .Where(m => m.Width == selectedRes.Width && m.Height == selectedRes.Height)
                .OrderByDescending(m => m.RefreshRateHz)
                .ToList();

            suppressComboEvents = true;
            try
            {
                refreshCombo.ItemsSource = rates;
                refreshCombo.SelectedItem = rates.FirstOrDefault();
            }
            finally
            {
                suppressComboEvents = false;
            }
        }

        void LoadModes()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            modes = _displayManager.GetAvailableModes(deviceName);
            var current = _displayManager.GetCurrentMode(deviceName);

            var resolutions = modes
                .GroupBy(m => (m.Width, m.Height))
                .Select(g => g.First())
                .ToList();

            suppressComboEvents = true;
            try
            {
                resolutionCombo.ItemsSource = resolutions;
                resolutionCombo.IsEnabled = resolutions.Count > 0;
                refreshCombo.IsEnabled = resolutions.Count > 0;

                if (current is not null)
                {
                    resolutionCombo.SelectedItem = resolutions
                        .FirstOrDefault(m => m.Width == current.Width && m.Height == current.Height);
                }

                resolutionCombo.SelectedItem ??= resolutions.FirstOrDefault();
                PopulateRefreshRates();

                if (current is not null)
                {
                    refreshCombo.SelectedItem = (refreshCombo.ItemsSource as IEnumerable<DisplayMode>)?
                        .FirstOrDefault(m => m.RefreshRateHz == current.RefreshRateHz) ?? refreshCombo.SelectedItem;
                }
            }
            finally
            {
                suppressComboEvents = false;
            }

            applyButton.IsEnabled = refreshCombo.SelectedItem is DisplayMode;

            // Programmatic IsChecked does not raise Click, so no event suppression needed.
            var hdr = _displayManager.GetHdrStatus(deviceName);
            if (hdr is { Supported: true })
            {
                _suppressHdrEvent = true;
                hdrSwitch.IsChecked = hdr.Enabled;
                _suppressHdrEvent = false;
                hdrSub.Text = hdr.Enabled ? "On · 10-bit" : "Off";
                hdrRow.Visibility = Visibility.Visible;
            }
        }

        _pendingModeLoads.Add(LoadModes);

        resolutionCombo.SelectionChanged += (_, _) =>
        {
            if (suppressComboEvents)
            {
                return;
            }

            PopulateRefreshRates();
            UpdatePendingState();
        };
        refreshCombo.SelectionChanged += (_, _) =>
        {
            if (suppressComboEvents)
            {
                return;
            }

            UpdatePendingState();
        };

        // Shows the staged change (and Apply) only when the pickers differ from
        // what the display is actually running.
        void UpdatePendingState()
        {
            if (refreshCombo.SelectedItem is not DisplayMode chosen)
            {
                confirmRow.HidePending();
                applyButton.IsEnabled = false;
                return;
            }

            var current = _displayManager.GetCurrentMode(deviceName);
            var changed = current is null || !current.Equals(chosen);

            applyButton.IsEnabled = changed;
            if (changed)
            {
                confirmRow.ShowPending($"Pending {chosen.Label}", applyButton);
            }
            else
            {
                confirmRow.HidePending();
            }

            AnimatePanelHeight();
        }

        applyButton.Click += async (_, _) =>
        {
            if (refreshCombo.SelectedItem is not DisplayMode chosen)
            {
                return;
            }

            // Capture the mode we're leaving so the countdown can put it back
            // if the new one is unusable (black screen, out-of-range monitor).
            var previous = _displayManager.GetCurrentMode(deviceName);

            try
            {
                applyButton.IsEnabled = false;
                SetBusy(true, $"Applying {chosen.Label}…");
                await Task.Run(() => _displayManager.ApplyDisplayMode(deviceName, chosen));
                RefreshMonitors();

                // The countdown must live in the toast, not in this card:
                // RefreshMonitors() above rebuilds every row, so anything inside
                // the card is detached from the visual tree by this point.
                if (previous is not null && !previous.Equals(chosen))
                {
                    StartRevertCountdown(deviceName, previous, chosen);
                }
                else
                {
                    ShowStatus(
                        $"{MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}: applied {chosen.Label}.",
                        success: true);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message, success: false);
            }
            finally
            {
                SetBusy(false);
                applyButton.IsEnabled = refreshCombo.SelectedItem is DisplayMode;
            }
        };

        async void HdrChanged(object sender, RoutedEventArgs args)
        {
            if (_suppressHdrEvent)
            {
                return;
            }

            var enable = hdrSwitch.IsChecked == true;
            try
            {
                hdrSwitch.IsEnabled = false;
                SetBusy(true, $"{(enable ? "Enabling" : "Disabling")} HDR…");
                await Task.Run(() => _displayManager.SetHdrEnabled(deviceName, enable));
                hdrSub.Text = enable ? "On · 10-bit" : "Off";
                ShowStatus(
                    $"{MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}: HDR {(enable ? "on" : "off")}.",
                    success: true);
            }
            catch (Exception ex)
            {
                _suppressHdrEvent = true;
                hdrSwitch.IsChecked = !enable;
                _suppressHdrEvent = false;
                ShowStatus(ex.Message, success: false);
            }
            finally
            {
                SetBusy(false);
                hdrSwitch.IsEnabled = true;
            }
        }

        hdrSwitch.Checked += HdrChanged;
        hdrSwitch.Unchecked += HdrChanged;

        // ── Collapsed row (fixed 56 px so adding a display never reflows) ──
        var glyph = BuildMonitorGlyph(monitor);

        var nameText = new TextBlock
        {
            Text = MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current),
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var primaryDot = new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = (Brush)FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = monitor.IsPrimary ? Visibility.Visible : Visibility.Collapsed,
        };

        var statusText = new TextBlock
        {
            Text = monitor.IsPrimary
                ? $"Primary · Display {monitor.Index + 1}"
                : $"Display {monitor.Index + 1} · click to make primary",
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            FontWeight = monitor.IsPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = monitor.IsPrimary
                ? (Brush)FindResource("AccentHoverBrush")
                : (Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var statusLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        statusLine.Children.Add(primaryDot);
        statusLine.Children.Add(statusText);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(nameText);
        textStack.Children.Add(statusLine);

        // Clicking anywhere on the row (except the chevron) sets primary.
        var rowContent = new Grid
        {
            Background = Brushes.Transparent,
            Height = 56,
            Cursor = monitor.IsPrimary ? Cursors.Arrow : Cursors.Hand,
        };
        rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(textStack, 1);
        textStack.Margin = new Thickness(12, 0, 8, 0);
        rowContent.Children.Add(glyph);
        rowContent.Children.Add(textStack);

        var chevron = new Button
        {
            Style = (Style)FindResource("CaptionButton"),
            Content = "",
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Show resolution, refresh and HDR",
        };
        AutomationProperties.SetName(chevron, $"Show display settings for {MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}");

        var headerGrid = new Grid { Margin = new Thickness(14, 0, 6, 0) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(rowContent, 0);
        Grid.SetColumn(chevron, 1);
        headerGrid.Children.Add(rowContent);
        headerGrid.Children.Add(chevron);

        // ── Expandable detail: mode controls for this display ──
        var detail = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(14, 0, 14, 12),
        };
        detail.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = (Brush)FindResource("HairlineBrush"),
            Margin = new Thickness(0, 0, 0, 12),
        });
        detail.Children.Add(grid);
        detail.Children.Add(hdrRow);
        detail.Children.Add(confirmRow.Root);

        if (!monitor.IsPrimary)
        {
            WireSetPrimaryActivation(rowContent, monitor.DeviceName,
                MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current));
        }

        chevron.Click += (_, _) =>
        {
            var opening = detail.Visibility != Visibility.Visible;

            // Always close whatever was open first, resetting its chevron —
            // collapsing the panel alone left a stale "expanded" glyph behind.
            CollapseOpenRow();

            if (opening)
            {
                detail.Visibility = Visibility.Visible;
                chevron.Content = "";
                chevron.ToolTip = "Hide display settings";
                _openDetail = detail;
                _openChevron = chevron;
                FadeIn(detail);
            }

            AnimatePanelHeight();
        };

        var content = new StackPanel();
        content.Children.Add(headerGrid);
        content.Children.Add(detail);

        var card = new Border
        {
            Background = (Brush)FindResource("CardBrush"),
            BorderBrush = monitor.IsPrimary
                ? new SolidColorBrush(Color.FromArgb(140, 0x4F, 0x8D, 0xFF))
                : (Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = content,
            Tag = detail,
        };

        return card;
    }

    private StackPanel? _openDetail;
    private Button? _openChevron;

    /// <summary>Closes the currently expanded display row and resets its chevron.</summary>
    private void CollapseOpenRow()
    {
        if (_openDetail is not null)
        {
            _openDetail.Visibility = Visibility.Collapsed;
        }

        if (_openChevron is not null)
        {
            _openChevron.Content = "";
            _openChevron.ToolTip = "Show resolution, refresh and HDR";
        }

        _openDetail = null;
        _openChevron = null;
    }

    /// <summary>
    /// Inline "Applied · reverting in Ns" strip shown under a mode editor after
    /// a resolution/refresh change. If the user does not press Keep before the
    /// countdown expires, the previous mode is restored — the safety net for a
    /// mode the monitor cannot actually display.
    /// </summary>
    /// <summary>
    /// Staged-change strip inside a display row: shows the mode that is about to
    /// be applied plus the Apply button, so Apply is only ever visible when
    /// there is something to apply.
    ///
    /// The post-apply revert countdown deliberately does NOT live here — the row
    /// is rebuilt by RefreshMonitors() the moment a mode is applied, which would
    /// detach this element from the visual tree. See StartRevertCountdown().
    /// </summary>
    private sealed class RevertConfirmRow
    {
        private readonly PanelWindow _owner;
        private readonly TextBlock _label;
        private readonly Grid _buttonHost;

        public Border Root { get; }

        public RevertConfirmRow(PanelWindow owner)
        {
            _owner = owner;

            _label = new TextBlock
            {
                FontFamily = (FontFamily)owner.FindResource("UiFont"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)owner.FindResource("SwapBrush"),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_label, 0);
            grid.Children.Add(_label);
            _buttonHost = grid;

            Root = new Border
            {
                Background = (Brush)owner.FindResource("MapSurfaceBrush"),
                BorderBrush = (Brush)owner.FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 12, 0, 0),
                Visibility = Visibility.Collapsed,
                Child = grid,
            };
        }

        public void ShowPending(string text, Button applyButton)
        {
            _label.Text = text;

            if (!_buttonHost.Children.Contains(applyButton))
            {
                Grid.SetColumn(applyButton, 1);
                applyButton.Margin = new Thickness(8, 0, 0, 0);
                applyButton.Height = 26;
                _buttonHost.Children.Add(applyButton);
            }

            applyButton.Visibility = Visibility.Visible;

            if (Root.Visibility != Visibility.Visible)
            {
                Root.Visibility = Visibility.Visible;
                _owner.FadeIn(Root);
            }
        }

        public void HidePending() => Root.Visibility = Visibility.Collapsed;
    }

    /// <summary>Brief accent→green flash on a button after a successful action.</summary>
    private void PulseApplySuccess(Button button)
    {
        if (!AnimationsEnabled)
        {
            return;
        }

        var accent = ((SolidColorBrush)FindResource("AccentBrush")).Color;
        var success = ((SolidColorBrush)FindResource("SuccessBrush")).Color;

        // Animate a private brush instance — animating the shared AccentBrush
        // would flash every accent-coloured control in the window.
        var brush = new SolidColorBrush(accent);
        button.Background = brush;

        var flash = new ColorAnimationUsingKeyFrames();
        flash.KeyFrames.Add(new LinearColorKeyFrame(success, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        flash.KeyFrames.Add(new LinearColorKeyFrame(success, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
        flash.KeyFrames.Add(new LinearColorKeyFrame(accent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
        flash.Completed += (_, _) =>
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            button.ClearValue(BackgroundProperty);
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, flash);
    }

    /// <summary>Short fade-in used by transient inline UI.</summary>
    private void FadeIn(UIElement element)
    {
        if (!AnimationsEnabled)
        {
            element.Opacity = 1;
            return;
        }

        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    /// <summary>Honours the Windows "show animations" accessibility setting.</summary>
    private static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    private bool _animatingHeight;

    /// <summary>
    /// Smoothly grows/shrinks the flyout when switching tabs instead of
    /// snapping. Height and Top animate together so the bottom edge stays
    /// pinned above the tray while the panel resizes.
    /// </summary>
    private void AnimatePanelHeight()
    {
        if (!IsVisible || _animatingHeight)
        {
            return;
        }

        if (!AnimationsEnabled)
        {
            PositionInWorkArea();
            return;
        }

        var from = ActualHeight;

        // Let WPF compute the natural height of the newly selected tab.
        SizeToContent = SizeToContent.Height;
        UpdateLayout();
        var to = ActualHeight;

        if (from <= 0 || to <= 0 || Math.Abs(to - from) < 2)
        {
            PositionInWorkArea();
            return;
        }

        var bottom = Top + from;

        _animatingHeight = true;
        SizeToContent = SizeToContent.Manual;
        Height = from;

        var duration = TimeSpan.FromMilliseconds(190);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var heightAnim = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        var topAnim = new DoubleAnimation(Top, bottom - to, duration) { EasingFunction = ease };

        heightAnim.Completed += (_, _) =>
        {
            // Hand control back to SizeToContent, then snap to exact placement.
            BeginAnimation(HeightProperty, null);
            BeginAnimation(TopProperty, null);
            SizeToContent = SizeToContent.Height;
            _animatingHeight = false;
            PositionInWorkArea();
        };

        BeginAnimation(TopProperty, topAnim);
        BeginAnimation(HeightProperty, heightAnim);
    }

    private UIElement BuildMonitorCard(MonitorInfo monitor, bool showSetPrimaryHint)
    {
        var numberedName = MonitorDisplayHelper.GetNumberedName(monitor, _settings.Current);
        var card = new Button
        {
            Style = (Style)FindResource("MonitorCard"),
            Height = showSetPrimaryHint && !monitor.IsPrimary ? 72 : 64,
            Margin = new Thickness(0, 0, 0, 8),
            IsEnabled = !monitor.IsPrimary && !_swapInProgress,
            ToolTip = monitor.IsPrimary
                ? $"{numberedName} is the primary display"
                : $"Make {numberedName} the primary display",
        };
        AutomationProperties.SetName(card, monitor.IsPrimary
            ? $"{numberedName}, current primary display"
            : $"Make {numberedName} the primary display");

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
            Text = numberedName,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = monitor.SpecsLabel,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        textStack.Children.Add(CreateRenameLink(monitor));

        if (showSetPrimaryHint && !monitor.IsPrimary)
        {
            textStack.Children.Add(new TextBlock
            {
                Text = "Click to set primary",
                FontFamily = (FontFamily)FindResource("UiFont"),
                FontSize = 10,
                Foreground = (Brush)FindResource("AccentHoverBrush"),
                Margin = new Thickness(0, 3, 0, 0),
            });
        }
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
            var deviceName = monitor.DeviceName;
            var displayName = MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current);
            WireSetPrimaryActivation(card, deviceName, displayName);
        }

        return card;
    }

    /// <summary>
    /// Routes single- and double-clicks on monitor cards/map tiles to set-primary,
    /// while leaving the inline Rename hyperlink interactive.
    /// </summary>
    private void WireSetPrimaryActivation(FrameworkElement target, string deviceName, string monitorName)
    {
        async void Activate(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsRenameClick(e.OriginalSource))
            {
                return;
            }

            if (_swapInProgress)
            {
                return;
            }

            e.Handled = true;
            await SetPrimaryAsync(deviceName, monitorName);
        }

        target.PreviewMouseLeftButtonUp += Activate;
        target.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount < 2 || e.ChangedButton != MouseButton.Left || IsRenameClick(e.OriginalSource))
            {
                return;
            }

            if (_swapInProgress)
            {
                return;
            }

            e.Handled = true;
            _ = SetPrimaryAsync(deviceName, monitorName);
        };
    }

    private static bool IsRenameClick(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Documents.Hyperlink)
            {
                return true;
            }
        }

        return false;
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

    private async Task SetPrimaryAsync(string deviceName, string monitorName)
    {
        if (_swapInProgress)
        {
            return;
        }

        try
        {
            _swapInProgress = true;
            SetBusy(true, $"Making {monitorName} primary…");

            // Remember what we're leaving so the toast can offer Undo — a
            // display swap is the change users most often want to take back.
            var previousPrimary = _displayManager.GetMonitors().FirstOrDefault(m => m.IsPrimary);

            var newPrimary = await Task.Run(() => _displayManager.SetPrimaryByDeviceName(deviceName));

            RefreshMonitors();
            PulsePrimaryTile();
            ShowStatus(
                $"{MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)} is now primary.",
                success: true,
                undoDeviceName: previousPrimary?.DeviceName);
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
        MonitorList.IsEnabled = !busy;
        MapHost.IsEnabled = !busy;

        if (busy && message != null)
        {
            ShowStatus(message, success: null);
        }
    }

    /// <summary>
    /// Inline confirmation strip at the foot of the panel. Replaces the old
    /// status line: feedback for a display change needs to be noticed, and a
    /// primary swap needs an Undo.
    /// </summary>
    private void ShowStatus(string message, bool? success, string? undoDeviceName = null)
    {
        _undoDeviceName = undoDeviceName;
        ToastText.Text = message;
        ToastUndo.Visibility = undoDeviceName is null ? Visibility.Collapsed : Visibility.Visible;

        var (icon, accent, background) = success switch
        {
            true => ("", (Brush)FindResource("SuccessBrush"), Color.FromArgb(0x1A, 0x46, 0xE0, 0xA0)),
            false => ("", (Brush)FindResource("ErrorBrush"), Color.FromArgb(0x1A, 0xFF, 0x73, 0x73)),
            null => ("", (Brush)FindResource("TextMutedBrush"), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
        };

        ToastIcon.Text = icon;
        ToastIcon.Foreground = accent;
        ToastHost.Background = new SolidColorBrush(background);
        ToastHost.BorderBrush = accent;

        if (ToastHost.Visibility != Visibility.Visible)
        {
            ToastHost.Visibility = Visibility.Visible;
            FadeIn(ToastHost);
        }

        // Auto-dismiss transient messages so the panel does not accumulate
        // stale state; errors stay until the next action.
        _toastTimer ??= new System.Windows.Threading.DispatcherTimer();
        _toastTimer.Stop();
        if (success != false)
        {
            _toastTimer.Interval = TimeSpan.FromSeconds(undoDeviceName is null ? 5 : 8);
            _toastTimer.Tick -= OnToastExpired;
            _toastTimer.Tick += OnToastExpired;
            _toastTimer.Start();
        }

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

    private void OnToastExpired(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        _undoDeviceName = null;
        ToastHost.Visibility = Visibility.Collapsed;
    }

    // ─────────── Post-apply revert countdown (lives in the toast) ───────────

    private System.Windows.Threading.DispatcherTimer? _revertTimer;
    private string _revertDeviceName = string.Empty;
    private string _revertAppliedLabel = string.Empty;
    private DisplayMode? _revertPreviousMode;
    private int _revertRemaining;

    private const int RevertCountdownSeconds = 12;

    /// <summary>
    /// After a resolution/refresh change, counts down and restores the previous
    /// mode unless the user confirms — the safety net for a mode the monitor
    /// cannot actually display. Hosted in the toast because the per-display card
    /// is rebuilt by RefreshMonitors() as soon as the mode is applied.
    /// </summary>
    private void StartRevertCountdown(string deviceName, DisplayMode previous, DisplayMode applied)
    {
        _revertDeviceName = deviceName;
        _revertPreviousMode = previous;
        _revertAppliedLabel = applied.Label;
        _revertRemaining = RevertCountdownSeconds;

        _revertTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _revertTimer.Stop();
        _revertTimer.Tick -= OnRevertTick;
        _revertTimer.Tick += OnRevertTick;

        ShowRevertToast();
        _revertTimer.Start();
    }

    private void ShowRevertToast()
    {
        // Reuse the toast, but drive it directly so the auto-dismiss timer in
        // ShowStatus cannot hide the countdown out from under the user.
        _toastTimer?.Stop();
        _undoDeviceName = null;

        ToastText.Text = $"Applied {_revertAppliedLabel} · reverting in {_revertRemaining}s";
        ToastIcon.Text = "";
        var warn = (Brush)FindResource("SwapBrush");
        ToastIcon.Foreground = warn;
        ToastHost.BorderBrush = warn;
        ToastHost.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x95, 0x00));

        ToastActionText.Text = "Keep";
        ToastUndo.Visibility = Visibility.Visible;

        if (ToastHost.Visibility != Visibility.Visible)
        {
            ToastHost.Visibility = Visibility.Visible;
            FadeIn(ToastHost);
        }
    }

    private void OnRevertTick(object? sender, EventArgs e)
    {
        _revertRemaining--;
        if (_revertRemaining > 0)
        {
            ToastText.Text = $"Applied {_revertAppliedLabel} · reverting in {_revertRemaining}s";
            return;
        }

        RevertModeNow();
    }

    private void KeepMode()
    {
        _revertTimer?.Stop();
        _revertPreviousMode = null;
        ToastActionText.Text = "Undo";
        ShowStatus($"Kept {_revertAppliedLabel}.", success: true);
    }

    private async void RevertModeNow()
    {
        _revertTimer?.Stop();

        var previous = _revertPreviousMode;
        var device = _revertDeviceName;
        _revertPreviousMode = null;
        ToastActionText.Text = "Undo";

        if (previous is null || string.IsNullOrEmpty(device))
        {
            return;
        }

        try
        {
            await Task.Run(() => _displayManager.ApplyDisplayMode(device, previous));
            RefreshMonitors();
            ShowStatus($"Reverted to {previous.Label} — the change was not confirmed.", success: null);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
    }

    /// <summary>
    /// The toast's action link: confirms a pending mode change when a revert
    /// countdown is running, otherwise undoes the last primary change.
    /// </summary>
    private async void ToastUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_revertPreviousMode is not null)
        {
            KeepMode();
            return;
        }

        var device = _undoDeviceName;
        if (string.IsNullOrEmpty(device))
        {
            return;
        }

        _undoDeviceName = null;
        ToastUndo.Visibility = Visibility.Collapsed;

        try
        {
            var restored = await Task.Run(() => _displayManager.SetPrimaryByDeviceName(device));
            RefreshMonitors();
            ShowStatus(
                $"{MonitorDisplayHelper.GetDisplayName(restored, _settings.Current)} restored as primary.",
                success: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HelpEmail_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlLaunchHelper.TryOpenWebOrMailUrl(AppInfo.SupportMailtoUri))
        {
            ShowStatus($"Help: {AppInfo.SupportEmail}", success: null);
        }
    }

    /// <summary>Thread-safe status update callable from background services.</summary>
    public void ShowExternalStatus(string message, bool? success)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowExternalStatus(message, success));
            return;
        }

        ShowStatus(message, success);
    }

    /// <summary>Updates UI hints that mention the (rebindable) open-panel hotkey.</summary>
    public void RefreshHotkeyHints()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshHotkeyHints);
            return;
        }

        var shortcut = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        HideButton.ToolTip = shortcut == "None"
            ? "Hide to tray"
            : $"Hide to tray ({shortcut} to reopen)";
    }

    public void ShowUpdateBanner(UpdateInfo info)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowUpdateBanner(info));
            return;
        }

        _updateReleaseUrl = info.ReleaseUrl;
        _updateTag = info.LatestTag;
        UpdateBannerTitle.Text = $"DisplayPilot {info.LatestTag} is available";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    public void ShowWhatsNewBanner(string version)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowWhatsNewBanner(version));
            return;
        }

        if (!ChangelogService.ShouldShowWhatsNew(_settings.Current.LastSeenVersion, version))
        {
            WhatsNewBanner.Visibility = Visibility.Collapsed;
            return;
        }

        _whatsNewVersion = version.Trim().TrimStart('v', 'V');
        WhatsNewBannerTitle.Text = ChangelogService.BuildWhatsNewTitle(_whatsNewVersion);
        WhatsNewBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBannerLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(string.IsNullOrWhiteSpace(_updateReleaseUrl) ? UpdateService.ReleasesPage : _updateReleaseUrl);
    }

    private void UpdateWhatsNewLink_Click(object sender, RoutedEventArgs e)
    {
        DismissUpdateBanner();
        OpenChangelogForTag(_updateTag);
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        DismissUpdateBanner();
    }

    private void DismissUpdateBanner()
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(_updateTag))
        {
            _settings.Update(s => s.DismissedUpdateTag = _updateTag);
        }
    }

    private void WhatsNewBannerLink_Click(object sender, RoutedEventArgs e)
    {
        MarkWhatsNewSeen();
        OpenChangelogForVersion(_whatsNewVersion);
    }

    private void WhatsNewBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        MarkWhatsNewSeen();
    }

    private void MarkWhatsNewSeen()
    {
        WhatsNewBanner.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_whatsNewVersion))
        {
            return;
        }

        var version = _whatsNewVersion;
        if (string.Equals(
                _settings.Current.LastSeenVersion?.Trim().TrimStart('v', 'V'),
                version,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Update(s => s.LastSeenVersion = version);
    }

    private void OpenChangelogForTag(string tag)
    {
        var version = tag.Trim().TrimStart('v', 'V');
        OpenChangelogForVersion(version, tag);
    }

    private void OpenChangelogForVersion(string version, string? releaseTag = null)
    {
        var app = System.Windows.Application.Current as App;
        app?.OpenChangelog(version, releaseTag ?? $"v{version}");
    }

    /// <summary>Updates the auto-swap profile summary card on the flyout.</summary>
    public void RefreshProfilesSummary()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshProfilesSummary);
            return;
        }

        var profiles = _settings.Current.Profiles;
        var enabled = profiles.Count(p => p.Enabled);

        if (profiles.Count == 0)
        {
            ProfilesSummaryText.Text = "Switch primary when a game or app starts — launcher profiles supported";
            ProfilesManageHint.Text = "Add ›";
            ProfilesCard.ToolTip = "Add an auto-swap profile (pick a game exe or launcher like Steam)";
            return;
        }

        var first = profiles[0];
        var preview = profiles.Count == 1
            ? first.DisplayLabel
            : $"{first.DisplayLabel} + {profiles.Count - 1} more";

        ProfilesSummaryText.Text = enabled == profiles.Count
            ? $"{enabled} active — {preview}"
            : $"{enabled} of {profiles.Count} active — {preview}";
        ProfilesManageHint.Text = "Manage ›";
        ProfilesCard.ToolTip = "Open the profile manager to add, edit, or remove auto-swap profiles";
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ProfilesCard_Click(object sender, RoutedEventArgs e) =>
        ProfilesRequested?.Invoke(this, EventArgs.Empty);

    private void ViewLog_Click(object sender, RoutedEventArgs e) =>
        ViewLogRequested?.Invoke(this, EventArgs.Empty);

    private static void OpenUrl(string url) => UrlLaunchHelper.TryOpenWebUrl(url);

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
