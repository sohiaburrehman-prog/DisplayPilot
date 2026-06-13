using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Controls;

internal sealed class MonitorCard : Control
{
    private readonly MonitorInfo _monitor;
    private bool _hovered;
    private bool _selected;

    public event EventHandler<MonitorInfo>? CardClicked;

    public MonitorInfo Monitor => _monitor;

    public bool IsCardSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    public MonitorCard(MonitorInfo monitor)
    {
        _monitor = monitor;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Height = AppTheme.MonitorCardHeight;
        Cursor = Cursors.Hand;
        TabStop = true;
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
        if (Enabled)
        {
            CardClicked?.Invoke(this, _monitor);
        }
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
            : _selected
                ? AppTheme.SurfaceSelected
                : _hovered
                    ? AppTheme.SurfaceHover
                    : AppTheme.SurfaceElevated;

        AppTheme.FillRoundedRect(g, bounds, AppTheme.CardCornerRadius, fill);

        var borderColor = _selected
            ? AppTheme.BorderFocus
            : _hovered
                ? AppTheme.Blend(AppTheme.Border, AppTheme.BorderFocus, 0.45f)
                : AppTheme.BorderSubtle;

        var borderWidth = _selected ? 1.5f : 1f;
        AppTheme.DrawRoundedRect(g, bounds, AppTheme.CardCornerRadius, borderColor, borderWidth);

        if (_hovered && !_selected && Enabled)
        {
            var glowBounds = bounds;
            glowBounds.Inflate(-1, -1);
            AppTheme.DrawRoundedRect(g, glowBounds, AppTheme.CardCornerRadius, Color.FromArgb(30, AppTheme.AccentPrimary), 2f);
        }

        MonitorDrawing.DrawCardContent(g, _monitor, bounds, _selected, _hovered);
    }
}
