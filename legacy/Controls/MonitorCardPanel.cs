using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Controls;

internal sealed class MonitorCardPanel : UserControl
{
    private readonly FlowLayoutPanel _flow;
    private readonly Label _emptyLabel;
    private readonly List<MonitorCard> _cards = [];
    private int _selectedIndex = -1;

    public event EventHandler? SelectionChanged;
    public event EventHandler<MonitorInfo>? MonitorClicked;

    public MonitorCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;

        _flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        _emptyLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
            BackColor = Color.Transparent,
            Visible = false,
            Padding = new Padding(AppTheme.Spacing, AppTheme.Spacing * 2, AppTheme.Spacing, AppTheme.Spacing * 2)
        };

        Controls.Add(_emptyLabel);
        Controls.Add(_flow);
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _cards.Count
            ? _cards[_selectedIndex].Monitor
            : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetSelectedIndex(value, raiseEvent: true);
    }

    public void ClearItems()
    {
        _cards.Clear();
        _selectedIndex = -1;
        _flow.Controls.Clear();
        _emptyLabel.Visible = false;
        _flow.Visible = true;
        Invalidate();
    }

    public void AddItem(object item)
    {
        if (item is not MonitorInfo monitor)
        {
            ShowEmptyState(item.ToString() ?? "No monitors detected");
            return;
        }

        var card = new MonitorCard(monitor)
        {
            Width = Math.Max(Width - 4, 300),
            Margin = new Padding(0, 0, 0, AppTheme.Spacing)
        };
        card.CardClicked += OnCardClicked;
        _cards.Add(card);
        _flow.Controls.Add(card);
    }

    public void ShowEmptyState(string message)
    {
        _flow.Visible = false;
        _emptyLabel.Text = message;
        _emptyLabel.Visible = true;
        Invalidate();
    }

    private void OnCardClicked(object? sender, MonitorInfo monitor)
    {
        var index = _cards.FindIndex(c => ReferenceEquals(c.Monitor, monitor));
        if (index >= 0)
        {
            SetSelectedIndex(index, raiseEvent: true);
        }

        MonitorClicked?.Invoke(this, monitor);
    }

    private void SetSelectedIndex(int index, bool raiseEvent)
    {
        if (index < -1 || index >= _cards.Count)
        {
            return;
        }

        if (index == _selectedIndex)
        {
            return;
        }

        if (_selectedIndex >= 0 && _selectedIndex < _cards.Count)
        {
            _cards[_selectedIndex].IsCardSelected = false;
        }

        _selectedIndex = index;

        if (_selectedIndex >= 0 && _selectedIndex < _cards.Count)
        {
            _cards[_selectedIndex].IsCardSelected = true;
        }

        if (raiseEvent)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var card in _cards)
        {
            card.Width = Math.Max(Width - 4, 300);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        foreach (var card in _cards)
        {
            card.Enabled = Enabled;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_emptyLabel.Visible)
        {
            var g = e.Graphics;
            AppTheme.ConfigureGraphics(g);
            var bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            AppTheme.FillRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.Surface);
            AppTheme.DrawRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.BorderSubtle);
        }
    }
}
