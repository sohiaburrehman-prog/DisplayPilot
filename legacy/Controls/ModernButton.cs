using System.Drawing.Drawing2D;

namespace PrimaryDisplaySwap.Controls;

internal enum ModernButtonStyle
{
    Primary,
    Accent,
    Secondary,
    Ghost
}

internal sealed class ModernButton : Control
{
    private readonly ModernButtonStyle _style;
    private bool _hovered;
    private bool _pressed;

    public ModernButton(string text, ModernButtonStyle style = ModernButtonStyle.Secondary)
    {
        _style = style;
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Font = AppTheme.ButtonFont;
        ForeColor = Color.White;
        Height = 40;
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
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var bounds = ClientRectangle;
        bounds.Inflate(-1, -1);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var (normal, hover, pressed) = GetColors();
        var fill = !Enabled ? AppTheme.Secondary : _pressed ? pressed : _hovered ? hover : normal;

        using var path = AppTheme.CreateRoundedRect(bounds, AppTheme.ButtonCornerRadius);
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        if (Enabled && _style == ModernButtonStyle.Primary && (_hovered || Focused))
        {
            using var glowPen = new Pen(AppTheme.AccentPrimaryGlow, 2f);
            g.DrawPath(glowPen, path);
        }
        else if (Enabled && _style == ModernButtonStyle.Ghost && _hovered)
        {
            using var borderPen = new Pen(AppTheme.Border, 1f);
            g.DrawPath(borderPen, path);
        }

        var textColor = Enabled ? ForeColor : AppTheme.TextMuted;
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            bounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private (Color Normal, Color Hover, Color Pressed) GetColors() => _style switch
    {
        ModernButtonStyle.Primary => (
            AppTheme.AccentPrimary,
            AppTheme.AccentPrimaryHover,
            AppTheme.Blend(AppTheme.AccentPrimaryHover, Color.Black, 0.15f)),
        ModernButtonStyle.Accent => (
            AppTheme.AccentSwap,
            AppTheme.AccentSwapHover,
            AppTheme.Blend(AppTheme.AccentSwapHover, Color.Black, 0.15f)),
        ModernButtonStyle.Ghost => (
            Color.Transparent,
            AppTheme.SurfaceHover,
            AppTheme.Surface),
        _ => (
            AppTheme.Secondary,
            AppTheme.SecondaryHover,
            AppTheme.Blend(AppTheme.SecondaryHover, Color.Black, 0.12f))
    };
}
