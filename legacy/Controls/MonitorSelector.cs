using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Controls;

internal sealed class MonitorSelector : UserControl
{
    private readonly List<object> _items = [];
    private int _selectedIndex = -1;
    private bool _dropdownOpen;
    private bool _hovered;
    private MonitorDropdownForm? _dropdown;

    public event EventHandler? SelectedIndexChanged;

    public MonitorSelector()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex]
        : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= _items.Count || value == _selectedIndex)
            {
                return;
            }

            _selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearItems()
    {
        _items.Clear();
        _selectedIndex = -1;
        CloseDropdown();
        Invalidate();
    }

    public void AddItem(object item)
    {
        _items.Add(item);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled)
        {
            CloseDropdown();
        }

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || _items.Count == 0)
        {
            return;
        }

        ToggleDropdown();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;

        var fill = !Enabled
            ? AppTheme.Surface
            : _dropdownOpen || _hovered
                ? AppTheme.SurfaceHover
                : AppTheme.SurfaceElevated;

        AppTheme.FillRoundedRect(g, bounds, AppTheme.ButtonCornerRadius, fill);

        var borderColor = _dropdownOpen ? AppTheme.BorderFocus : AppTheme.Border;
        AppTheme.DrawRoundedRect(g, bounds, AppTheme.ButtonCornerRadius, borderColor);

        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            DrawMonitorRow(g, _items[_selectedIndex], new Rectangle(10, 0, bounds.Width - 34, bounds.Height), compact: false);
        }
        else if (_items.Count > 0)
        {
            TextRenderer.DrawText(
                g,
                "Select a monitor",
                AppTheme.BodyFont,
                new Rectangle(12, 0, bounds.Width - 36, bounds.Height),
                AppTheme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        DrawChevron(g, bounds);
    }

    private static void DrawChevron(Graphics g, Rectangle bounds)
    {
        var cx = bounds.Right - 18;
        var cy = bounds.Top + bounds.Height / 2;
        using var pen = new Pen(AppTheme.TextSecondary, 1.6f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
        g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
    }

    internal static void DrawMonitorRow(Graphics g, object item, Rectangle bounds, bool compact)
    {
        var isPrimary = false;
        string title;
        string subtitle;

        if (item is MonitorInfo monitor)
        {
            isPrimary = monitor.IsPrimary;
            title = monitor.Name;
            subtitle = $"{monitor.Width}×{monitor.Height}";
        }
        else
        {
            title = item.ToString() ?? string.Empty;
            subtitle = string.Empty;
        }

        var iconBounds = new Rectangle(bounds.Left, bounds.Top + (bounds.Height - 22) / 2, 22, 22);
        DrawMonitorIcon(g, iconBounds, isPrimary);

        var textLeft = iconBounds.Right + 8;
        var titleHeight = compact ? bounds.Height : bounds.Height / 2 + 2;

        TextRenderer.DrawText(
            g,
            title,
            compact ? AppTheme.BodyFont : AppTheme.BodySemiboldFont,
            new Rectangle(textLeft, bounds.Top + (compact ? 0 : 4), bounds.Width - textLeft - 4, titleHeight),
            AppTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (!compact && !string.IsNullOrEmpty(subtitle))
        {
            TextRenderer.DrawText(
                g,
                subtitle,
                AppTheme.CaptionFont,
                new Rectangle(textLeft, bounds.Top + bounds.Height / 2 - 2, bounds.Width - textLeft - 56, bounds.Height / 2),
                AppTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        if (isPrimary)
        {
            DrawPrimaryBadge(g, new Rectangle(bounds.Right - 52, bounds.Top + (bounds.Height - 18) / 2, 48, 18));
        }
    }

    private static void DrawMonitorIcon(Graphics g, Rectangle bounds, bool isPrimary)
    {
        AppTheme.FillRoundedRect(g, bounds, 5, AppTheme.BackgroundElevated);
        AppTheme.DrawRoundedRect(g, bounds, 5, AppTheme.Border);

        var screen = new Rectangle(bounds.Left + 4, bounds.Top + 4, bounds.Width - 8, bounds.Height - 10);
        using (var brush = new SolidBrush(isPrimary ? AppTheme.AccentPrimary : AppTheme.Secondary))
        {
            g.FillRectangle(brush, screen);
        }

        var stand = new Rectangle(bounds.Left + bounds.Width / 2 - 3, bounds.Bottom - 4, 6, 3);
        using (var brush = new SolidBrush(AppTheme.Border))
        {
            g.FillRectangle(brush, stand);
        }
    }

    private static void DrawPrimaryBadge(Graphics g, Rectangle bounds)
    {
        AppTheme.FillRoundedRect(g, bounds, 9, Color.FromArgb(40, AppTheme.AccentPrimary));
        AppTheme.DrawRoundedRect(g, bounds, 9, AppTheme.AccentPrimary, 1f);
        TextRenderer.DrawText(
            g,
            "★ PRIMARY",
            AppTheme.BadgeFont,
            bounds,
            AppTheme.AccentPrimaryHover,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void ToggleDropdown()
    {
        if (_dropdownOpen)
        {
            CloseDropdown();
        }
        else
        {
            OpenDropdown();
        }
    }

    private void OpenDropdown()
    {
        CloseDropdown();

        _dropdown = new MonitorDropdownForm(this, _items, _selectedIndex);
        _dropdown.ItemSelected += (_, index) =>
        {
            SelectedIndex = index;
            CloseDropdown();
        };
        _dropdown.FormClosed += (_, _) =>
        {
            _dropdownOpen = false;
            _dropdown = null;
            Invalidate();
        };

        var screenPoint = PointToScreen(new Point(0, Height + 2));
        _dropdown.ShowAt(screenPoint, Width);
        _dropdownOpen = true;
        Invalidate();
    }

    private void CloseDropdown()
    {
        if (_dropdown is null || _dropdown.IsDisposed)
        {
            _dropdownOpen = false;
            _dropdown = null;
            return;
        }

        _dropdown.Close();
        _dropdown.Dispose();
        _dropdown = null;
        _dropdownOpen = false;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseDropdown();
        }

        base.Dispose(disposing);
    }

    private sealed class MonitorDropdownForm : Form
    {
        private readonly MonitorSelector _owner;
        private readonly List<object> _items;
        private int _hoverIndex;

        public event EventHandler<int>? ItemSelected;

        public MonitorDropdownForm(MonitorSelector owner, List<object> items, int selectedIndex)
        {
            _owner = owner;
            _items = items;
            _hoverIndex = selectedIndex;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = AppTheme.SurfaceElevated;
            Padding = new Padding(4);

            var itemHeight = 44;
            Height = Math.Min(220, 8 + _items.Count * itemHeight);
            Width = owner.Width;

            Deactivate += (_, _) => Close();
        }

        public void ShowAt(Point location, int width)
        {
            Location = location;
            Width = width;
            Show(_owner.FindForm());
            Activate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            AppTheme.ConfigureGraphics(g);

            var bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            AppTheme.FillRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.SurfaceElevated);
            AppTheme.DrawRoundedRect(g, bounds, AppTheme.CardCornerRadius, AppTheme.Border);

            var itemHeight = 44;
            for (var i = 0; i < _items.Count; i++)
            {
                var row = new Rectangle(6, 6 + i * itemHeight, bounds.Width - 10, itemHeight - 4);
                if (i == _hoverIndex)
                {
                    AppTheme.FillRoundedRect(g, row, 8, AppTheme.SurfaceHover);
                }

                MonitorSelector.DrawMonitorRow(g, _items[i], row, compact: true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var itemHeight = 44;
            var index = (e.Y - 6) / itemHeight;
            if (index >= 0 && index < _items.Count && index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var itemHeight = 44;
            var index = (e.Y - 6) / itemHeight;
            if (index >= 0 && index < _items.Count)
            {
                ItemSelected?.Invoke(this, index);
            }
        }
    }
}
