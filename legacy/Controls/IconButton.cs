namespace PrimaryDisplaySwap.Controls;

internal sealed class IconButton : Control
{
    private bool _hovered;

    public string IconGlyph { get; set; } = "—";
    public string ToolTipText { get; set; } = string.Empty;

    private ToolTip? _toolTip;

    public IconButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Size = new Size(28, 28);
        Cursor = Cursors.Hand;
        Font = new Font(AppTheme.BodyFont.FontFamily, 11F, FontStyle.Regular);
        ForeColor = AppTheme.TextSecondary;
        TabStop = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!string.IsNullOrEmpty(ToolTipText))
        {
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(this, ToolTipText);
        }
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var bounds = ClientRectangle;
        bounds.Inflate(-2, -2);

        if (_hovered)
        {
            AppTheme.FillRoundedRect(g, bounds, 6, AppTheme.SurfaceHover);
        }

        TextRenderer.DrawText(
            g,
            IconGlyph,
            Font,
            ClientRectangle,
            _hovered ? AppTheme.TextPrimary : ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip?.Dispose();
        }

        base.Dispose(disposing);
    }
}
