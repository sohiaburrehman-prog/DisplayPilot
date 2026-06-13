using PrimaryDisplaySwap.Controls;
using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

namespace PrimaryDisplaySwap;

internal sealed class MiniControlForm : Form
{
    private readonly DisplayManager _displayManager;
    private readonly StartupService _startupService;
    private readonly Icon _appIcon;
    private readonly MonitorCardPanel _monitorPanel;
    private readonly ModernButton _makePrimaryButton;
    private readonly ModernButton _swapButton;
    private readonly ToggleSwitch _startupToggle;
    private readonly StatusBar _statusBar;
    private readonly HeaderPanel _header;

    public event EventHandler? HideToTrayRequested;

    private bool _allowClose;
    private bool _suppressStartupEvent;
    private int _monitorCount;

    public void AllowClose()
    {
        _allowClose = true;
    }

    public MiniControlForm(DisplayManager displayManager, StartupService startupService, Icon appIcon)
    {
        _displayManager = displayManager;
        _startupService = startupService;
        _appIcon = (Icon)appIcon.Clone();

        Text = AppTheme.AppName;
        Icon = _appIcon;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AppTheme.ApplyDarkForm(this);
        FormChromeHelper.ApplyBorderlessChrome(this);

        _header = new HeaderPanel();
        _header.HideButton.Click += (_, _) => HideToTrayRequested?.Invoke(this, EventArgs.Empty);
        EnableHeaderDrag(_header);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(AppTheme.Spacing * 2, AppTheme.Spacing * 2, AppTheme.Spacing * 2, AppTheme.Spacing)
        };

        var displaysLabel = new Label
        {
            Text = "DISPLAYS",
            Dock = DockStyle.Top,
            Height = 18,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            Font = AppTheme.SectionFont,
            Margin = new Padding(0, 0, 0, AppTheme.Spacing)
        };

        _monitorPanel = new MonitorCardPanel
        {
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, AppTheme.Spacing * 2)
        };
        _monitorPanel.MonitorClicked += (_, monitor) => OnMonitorCardClicked(monitor);
        _monitorPanel.SelectionChanged += (_, _) => UpdateActionButtons();

        _makePrimaryButton = new ModernButton("Make Primary", ModernButtonStyle.Primary)
        {
            Dock = DockStyle.Top,
            Height = 40,
            Margin = new Padding(0, 0, 0, AppTheme.Spacing),
            Visible = false
        };
        _makePrimaryButton.Click += (_, _) => SetSelectedPrimary();

        _swapButton = new ModernButton("Swap Displays", ModernButtonStyle.Accent)
        {
            Dock = DockStyle.Top,
            Height = 44,
            Margin = new Padding(0, 0, 0, AppTheme.Spacing * 2),
            Visible = false
        };
        _swapButton.Click += (_, _) => QuickSwap();

        var settingsCard = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, AppTheme.Spacing)
        };

        _startupToggle = new ToggleSwitch
        {
            LabelText = "Start with Windows",
            Dock = DockStyle.Fill
        };
        _startupToggle.CheckedChanged += (_, _) => ToggleStartup();
        settingsCard.Controls.Add(_startupToggle);

        _statusBar = new StatusBar
        {
            Dock = DockStyle.Bottom,
            Margin = new Padding(0, AppTheme.Spacing, 0, 0)
        };

        content.Controls.Add(_statusBar);
        content.Controls.Add(settingsCard);
        content.Controls.Add(_swapButton);
        content.Controls.Add(_makePrimaryButton);
        content.Controls.Add(_monitorPanel);
        content.Controls.Add(displaysLabel);

        Controls.Add(content);
        Controls.Add(_header);

        Load += (_, _) =>
        {
            PositionInCorner();
            UpdateFormHeight();
        };

        content.Resize += (_, _) => UpdateMonitorPanelHeight();
    }

    public void RefreshMonitors()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshMonitors);
            return;
        }

        _suppressStartupEvent = true;
        _startupToggle.Checked = _startupService.IsEnabled;
        _suppressStartupEvent = false;

        _monitorPanel.ClearItems();

        try
        {
            var monitors = _displayManager.GetMonitors();
            _monitorCount = monitors.Count;

            if (monitors.Count <= 1)
            {
                _monitorPanel.ShowEmptyState(
                    monitors.Count == 0
                        ? "No displays detected"
                        : "Only one monitor connected.\nConnect another display to swap.");
                _monitorPanel.Enabled = false;
                _makePrimaryButton.Enabled = false;
                _makePrimaryButton.Visible = false;
                _swapButton.Enabled = false;
                _swapButton.Visible = false;
                UpdateFormHeight();
                return;
            }

            foreach (var monitor in monitors)
            {
                _monitorPanel.AddItem(monitor);
            }

            var primaryIndex = monitors.ToList().FindIndex(m => m.IsPrimary);
            _monitorPanel.SelectedIndex = primaryIndex >= 0 ? primaryIndex : 0;
            _monitorPanel.Enabled = true;
            _makePrimaryButton.Enabled = true;
            _swapButton.Enabled = monitors.Count == 2;
            _swapButton.Visible = monitors.Count == 2;
            UpdateActionButtons();
            UpdateFormHeight();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
            _monitorPanel.ShowEmptyState("Could not read displays");
            _monitorPanel.Enabled = false;
            _makePrimaryButton.Enabled = false;
            _swapButton.Enabled = false;
            UpdateFormHeight();
        }
    }

    private void OnMonitorCardClicked(MonitorInfo monitor)
    {
        if (!monitor.IsPrimary)
        {
            SetPrimaryForMonitor(monitor);
        }
    }

    private void SetSelectedPrimary()
    {
        if (_monitorPanel.SelectedItem is not MonitorInfo monitor)
        {
            return;
        }

        SetPrimaryForMonitor(monitor);
    }

    private void SetPrimaryForMonitor(MonitorInfo monitor)
    {
        if (monitor.IsPrimary)
        {
            ShowStatus($"{monitor.Name} is already primary", success: null);
            return;
        }

        try
        {
            var newPrimary = _displayManager.SetPrimaryMonitor(monitor.Index);
            RefreshMonitors();
            ShowStatus($"Primary set to {newPrimary.Name}", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
    }

    private void QuickSwap()
    {
        try
        {
            var newPrimary = _displayManager.SwapPrimaryBetweenTwoMonitors();
            RefreshMonitors();
            ShowStatus($"Swapped — now {newPrimary.Name}", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
        }
    }

    private void UpdateActionButtons()
    {
        if (_monitorPanel.SelectedItem is not MonitorInfo selected)
        {
            _makePrimaryButton.Visible = false;
            return;
        }

        var showMakePrimary = _monitorCount > 2 && !selected.IsPrimary;
        _makePrimaryButton.Visible = showMakePrimary;
        _makePrimaryButton.Enabled = showMakePrimary;
    }

    private void UpdateMonitorPanelHeight()
    {
        if (_monitorCount <= 1)
        {
            _monitorPanel.Height = 120;
            return;
        }

        var cardHeight = AppTheme.MonitorCardHeight + AppTheme.Spacing;
        var visibleCards = Math.Min(_monitorCount, 3);
        var scrollExtra = _monitorCount > 3 ? AppTheme.Spacing * 2 : 0;
        _monitorPanel.Height = visibleCards * cardHeight + scrollExtra;
    }

    private void UpdateFormHeight()
    {
        UpdateMonitorPanelHeight();

        var height = _header.Height;
        height += AppTheme.Spacing * 2; // top padding
        height += 18 + AppTheme.Spacing; // section label
        height += _monitorPanel.Height + AppTheme.Spacing * 2;

        if (_makePrimaryButton.Visible)
        {
            height += 40 + AppTheme.Spacing;
        }

        if (_swapButton.Visible)
        {
            height += 44 + AppTheme.Spacing * 2;
        }

        height += 40 + AppTheme.Spacing; // settings
        height += 32 + AppTheme.Spacing; // status
        height += AppTheme.Spacing; // bottom padding

        var size = new Size(AppTheme.FormWidth, height);
        MinimumSize = size;
        MaximumSize = size;
        Size = size;
        PositionInCorner();
    }

    public void ShowSwapStatus(string message, bool success) => ShowStatus(message, success);

    private void ShowStatus(string message, bool? success)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowStatus(message, success));
            return;
        }

        _statusBar.SetStatus(message, success);
        if (success == false)
        {
            AppLogger.Log($"Panel error: {message}");
        }
    }

    private void ToggleStartup()
    {
        if (_suppressStartupEvent)
        {
            return;
        }

        try
        {
            _startupService.SetEnabled(_startupToggle.Checked);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, success: false);
            _suppressStartupEvent = true;
            _startupToggle.Checked = _startupService.IsEnabled;
            _suppressStartupEvent = false;
        }
    }

    private void EnableHeaderDrag(Control header)
    {
        FormChromeHelper.EnableDragOnControl(header, this);
        foreach (Control child in header.Controls)
        {
            if (child is IconButton)
            {
                continue;
            }

            FormChromeHelper.EnableDragOnControl(child, this);
        }
    }

    private void PositionInCorner()
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
        Location = new Point(
            workArea.Right - Width - 16,
            workArea.Bottom - Height - 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);
        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        AppTheme.DrawRoundedRect(g, bounds, AppTheme.CornerRadius, AppTheme.BorderSubtle, 1f);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTrayRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
