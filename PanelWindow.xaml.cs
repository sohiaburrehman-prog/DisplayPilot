using System.Diagnostics;
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
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Rectangle = System.Windows.Shapes.Rectangle;

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
    private bool _swapInProgress;
    private bool _loadingModeEditors;
    private string _swapButtonIdleText = "Swap Displays";
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

        PanelTabs.SelectionChanged += (_, _) => LoadPendingModeEditors();

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

        RefreshProfilesSummary();

        MonitorList.Children.Clear();
        MoreMonitorList.Children.Clear();
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
            SwapButton.Visibility = Visibility.Collapsed;
            MapHost.Visibility = Visibility.Collapsed;
            _lastMapMonitors = Array.Empty<MonitorInfo>();
            MoreEmptyStateHost.Visibility = Visibility.Visible;
            return;
        }

        MoreEmptyStateHost.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var monitor in monitors)
        {
            MoreMonitorList.Children.Add(BuildResolutionEntry(monitor));
        }

        LoadPendingModeEditors();

        if (monitors.Count <= 1)
        {
            EmptyStateTitle.Text = monitors.Count == 0
                ? "No displays detected"
                : "Only one monitor connected";
            EmptyState.Text = monitors.Count == 0
                ? "Windows did not report any active displays."
                : "Connect another display to swap or change primary.";
            EmptyStateHost.Visibility = Visibility.Visible;
            SwapButton.Visibility = Visibility.Collapsed;
            MapHost.Visibility = Visibility.Collapsed;
            _lastMapMonitors = Array.Empty<MonitorInfo>();
            return;
        }

        EmptyStateHost.Visibility = Visibility.Collapsed;
        SwapButton.Visibility = monitors.Count == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (monitors.Count == 2)
        {
            var primary = monitors.First(m => m.IsPrimary);
            var other = monitors.First(m => !m.IsPrimary);
            var primaryName = MonitorDisplayHelper.GetDisplayName(primary, _settings.Current);
            var otherName = MonitorDisplayHelper.GetDisplayName(other, _settings.Current);
            _swapButtonIdleText = $"Swap {primary.Index + 1} ↔ {other.Index + 1}";
            SwapButton.ToolTip = $"{primaryName} ↔ {otherName}";
            if (!_swapInProgress)
            {
                SwapLabel.Text = _swapButtonIdleText;
            }
        }

        BuildArrangementMap(monitors);
        _lastMapMonitors = monitors;
        MapHost.Visibility = Visibility.Visible;

        foreach (var monitor in monitors)
        {
            MonitorList.Children.Add(BuildMonitorCard(monitor, monitors.Count > 2));
        }

        if (monitors.Count > 2)
        {
            ShowStatus("Click a monitor or the arrangement map to set primary.", success: null);
        }
        else if (string.IsNullOrWhiteSpace(StatusText.Text) || StatusText.Text.StartsWith("Click a monitor"))
        {
            StatusText.Text = string.Empty;
        }

        if (IsVisible && PanelTabs.SelectedIndex == 0)
        {
            PlayCardStagger();
        }
    }

    /// <summary>Switches to the Advanced tab (resolution &amp; profiles).</summary>
    public void FocusAdvancedTab()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(FocusAdvancedTab);
            return;
        }

        PanelTabs.SelectedIndex = 1;
    }

    private void LoadPendingModeEditors()
    {
        if (PanelTabs.SelectedIndex != 1 || _pendingModeLoads.Count == 0 || _loadingModeEditors)
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

            var index = monitor.Index;
            var displayName = MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current);
            var idleBrush = monitor.IsPrimary
                ? (Brush)FindResource("ScreenGradientBrush")
                : (Brush)FindResource("ScreenIdleBrush");

            if (!monitor.IsPrimary)
            {
                WireSetPrimaryActivation(screen, index, displayName);
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

    /// <summary>When exactly two tiles are on the map, animates them sliding
    /// past each other to trade places — the visual counterpart of a swap.
    /// Completes (roughly) when the slide finishes so the caller can rebuild.</summary>
    private Task PlayMapSwapAnimationAsync()
    {
        var tiles = ArrangementCanvas.Children.OfType<Border>()
            .Where(b => b.Tag is MonitorInfo)
            .ToList();

        if (tiles.Count != 2)
        {
            return Task.CompletedTask;
        }

        var a = tiles[0];
        var b = tiles[1];
        var aLeft = Canvas.GetLeft(a);
        var bLeft = Canvas.GetLeft(b);
        if (double.IsNaN(aLeft) || double.IsNaN(bLeft))
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        var duration = TimeSpan.FromMilliseconds(320);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Lift the tiles a touch and arc them so they don't just overlap flatly.
        foreach (var tile in tiles)
        {
            tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            tile.RenderTransform = new TranslateTransform();
        }

        System.Windows.Controls.Panel.SetZIndex(a, 10);

        var slideA = new DoubleAnimation(aLeft, bLeft, duration) { EasingFunction = ease };
        var slideB = new DoubleAnimation(bLeft, aLeft, duration) { EasingFunction = ease };

        var arc = new DoubleAnimation
        {
            From = 0,
            To = -10,
            Duration = TimeSpan.FromMilliseconds(160),
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        slideB.Completed += (_, _) => tcs.TrySetResult();
        a.BeginAnimation(Canvas.LeftProperty, slideA);
        b.BeginAnimation(Canvas.LeftProperty, slideB);
        ((TranslateTransform)a.RenderTransform).BeginAnimation(TranslateTransform.YProperty, arc);
        ((TranslateTransform)b.RenderTransform).BeginAnimation(TranslateTransform.YProperty, (DoubleAnimation)arc.Clone());

        return tcs.Task;
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
    private UIElement BuildResolutionEntry(MonitorInfo monitor)
    {
        var deviceName = monitor.DeviceName;

        var resolutionCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            Margin = new Thickness(0, 0, 4, 0),
            MinWidth = 100,
            IsEnabled = false,
        };
        var refreshCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            Width = 84,
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = false,
        };
        var applyButton = new Button
        {
            Style = (Style)FindResource("AccentMiniButton"),
            Content = "Apply",
            MinWidth = 58,
            Padding = new Thickness(10, 0, 10, 0),
            IsEnabled = false,
        };

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(resolutionCombo, 0);
        Grid.SetColumn(refreshCombo, 1);
        Grid.SetColumn(applyButton, 2);
        grid.Children.Add(resolutionCombo);
        grid.Children.Add(refreshCombo);
        grid.Children.Add(applyButton);

        // HDR toggle: hidden unless the monitor reports HDR support.
        var hdrCheck = new CheckBox
        {
            Style = (Style)FindResource("DarkCheckBox"),
            Content = "HDR",
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };

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
                refreshCombo.DisplayMemberPath = nameof(DisplayMode.RefreshRateHz);
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
                resolutionCombo.DisplayMemberPath = nameof(DisplayMode.ResolutionLabel);
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
                hdrCheck.IsChecked = hdr.Enabled;
                hdrCheck.Visibility = Visibility.Visible;
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
            applyButton.IsEnabled = refreshCombo.SelectedItem is DisplayMode;
        };
        refreshCombo.SelectionChanged += (_, _) =>
        {
            if (suppressComboEvents)
            {
                return;
            }

            applyButton.IsEnabled = refreshCombo.SelectedItem is DisplayMode;
        };

        applyButton.Click += async (_, _) =>
        {
            if (refreshCombo.SelectedItem is not DisplayMode chosen)
            {
                return;
            }

            try
            {
                applyButton.IsEnabled = false;
                SetBusy(true, $"Applying {chosen.Label}…");
                await Task.Run(() => _displayManager.ApplyDisplayMode(deviceName, chosen));
                RefreshMonitors();
                ShowStatus($"{MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}: applied {chosen.Label}.", success: true);
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

        hdrCheck.Click += async (_, _) =>
        {
            var enable = hdrCheck.IsChecked == true;
            try
            {
                hdrCheck.IsEnabled = false;
                SetBusy(true, $"{(enable ? "Enabling" : "Disabling")} HDR…");
                await Task.Run(() => _displayManager.SetHdrEnabled(deviceName, enable));
                ShowStatus(
                    $"{MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}: HDR {(enable ? "on" : "off")}.",
                    success: true);
            }
            catch (Exception ex)
            {
                hdrCheck.IsChecked = !enable;
                ShowStatus(ex.Message, success: false);
            }
            finally
            {
                SetBusy(false);
                hdrCheck.IsEnabled = true;
            }
        };

        var card = new Border
        {
            Background = (Brush)FindResource("CardBrush"),
            BorderBrush = (Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = MonitorDisplayHelper.GetNumberedName(monitor, _settings.Current),
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        header.Children.Add(new TextBlock
        {
            Text = monitor.SpecsLabel,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        header.Children.Add(CreateRenameLink(monitor));

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(grid);
        content.Children.Add(hdrCheck);
        card.Child = content;
        return card;
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
            var index = monitor.Index;
            var displayName = MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current);
            WireSetPrimaryActivation(card, index, displayName);
        }

        return card;
    }

    /// <summary>
    /// Routes single- and double-clicks on monitor cards/map tiles to set-primary,
    /// while leaving the inline Rename hyperlink interactive.
    /// </summary>
    private void WireSetPrimaryActivation(FrameworkElement target, int monitorIndex, string monitorName)
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
            await SetPrimaryAsync(monitorIndex, monitorName);
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
            _ = SetPrimaryAsync(monitorIndex, monitorName);
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

            await PlayMapSwapAnimationAsync();
            RefreshMonitors();
            PulsePrimaryTile();
            ShowStatus($"Swapped — {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)} is now primary.", success: true);
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
            PulsePrimaryTile();
            ShowStatus($"Primary set to {MonitorDisplayHelper.GetDisplayName(newPrimary, _settings.Current)}.", success: true);
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
        MoreMonitorList.IsEnabled = !busy;
        MapHost.IsEnabled = !busy;
        PanelTabs.IsEnabled = !busy;

        if (busy)
        {
            SwapLabel.Text = message ?? "Working…";
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            SwapIconRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            SwapLabel.Text = _swapButtonIdleText;
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

    private void HelpEmail_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlLaunchHelper.TryOpenWebOrMailUrl(AppInfo.SupportMailtoUri))
        {
            StatusText.Text = $"Help: {AppInfo.SupportEmail}";
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
