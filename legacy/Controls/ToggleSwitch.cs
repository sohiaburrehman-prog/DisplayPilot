namespace PrimaryDisplaySwap.Controls;

internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hovered;
    private bool _pressed;

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

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Height = 32;
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
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
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

        var trackWidth = 40;
        var trackHeight = 22;
        var track = new Rectangle(Width - trackWidth - 2, (Height - trackHeight) / 2, trackWidth, trackHeight);

        var trackFill = !Enabled
            ? AppTheme.Surface
            : _checked
                ? _hovered ? AppTheme.AccentPrimaryHover : AppTheme.AccentPrimary
                : _hovered ? AppTheme.SurfaceHover : AppTheme.SurfaceElevated;

        AppTheme.FillRoundedRect(g, track, trackHeight / 2, trackFill);

        if (!_checked)
        {
            AppTheme.DrawRoundedRect(g, track, trackHeight / 2, AppTheme.Border);
        }

        var knobSize = trackHeight - 4;
        var knobX = _checked
            ? track.Right - knobSize - 2
            : track.Left + 2;
        if (_pressed)
        {
            knobX += _checked ? -1 : 1;
        }

        var knob = new Rectangle(knobX, track.Top + 2, knobSize, knobSize);
        using (var brush = new SolidBrush(Enabled ? Color.White : AppTheme.TextMuted))
        {
            g.FillEllipse(brush, knob);
        }

        var labelColor = _hovered && Enabled ? AppTheme.TextPrimary : ForeColor;
        TextRenderer.DrawText(
            g,
            LabelText,
            Font,
            new Rectangle(0, 0, Width - trackWidth - 12, Height),
            labelColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
