namespace PrimaryDisplaySwap.Controls;

internal sealed class ModernCheckBox : Control
{
    private bool _checked;
    private bool _hovered;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public string LabelText { get; set; } = string.Empty;

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Height = 28;
        Cursor = Cursors.Hand;
        Font = AppTheme.CaptionFont;
        ForeColor = AppTheme.TextSecondary;
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
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var box = new Rectangle(2, (Height - 18) / 2, 18, 18);
        var boxFill = _checked
            ? AppTheme.AccentPrimary
            : _hovered ? AppTheme.SurfaceHover : AppTheme.Surface;

        AppTheme.FillRoundedRect(g, box, 5, boxFill);
        AppTheme.DrawRoundedRect(g, box, 5, _checked ? AppTheme.AccentPrimary : AppTheme.Border);

        if (_checked)
        {
            using var pen = new Pen(Color.White, 2f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            g.DrawLine(pen, box.Left + 4, box.Top + 9, box.Left + 7, box.Bottom - 5);
            g.DrawLine(pen, box.Left + 7, box.Bottom - 5, box.Right - 4, box.Top + 5);
        }

        TextRenderer.DrawText(
            g,
            LabelText,
            Font,
            new Rectangle(box.Right + 8, 0, Width - box.Right - 10, Height),
            _hovered ? AppTheme.TextPrimary : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
