using System.Drawing.Drawing2D;

namespace PrimaryDisplaySwap.Controls;

internal sealed class HeaderPanel : Panel
{
    private readonly PictureBox _iconBox;
    private readonly Label _titleLabel;
    private readonly Label _versionLabel;
    private readonly Panel _dragGrip;
    public readonly IconButton HideButton;

    public HeaderPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        Dock = DockStyle.Top;
        Height = 48;
        BackColor = Color.Transparent;
        Padding = new Padding(0);

        _iconBox = new PictureBox
        {
            Size = new Size(24, 24),
            Location = new Point(16, 12),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = AppIconHelper.LoadAppImage(24)
        };

        _titleLabel = new Label
        {
            Text = AppTheme.AppName,
            AutoSize = false,
            Location = new Point(48, 10),
            Size = new Size(200, 18),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.TextPrimary,
            Font = AppTheme.TitleFont,
            BackColor = Color.Transparent
        };

        _versionLabel = new Label
        {
            Text = $"v{AppTheme.AppVersion}",
            AutoSize = false,
            Location = new Point(48, 28),
            Size = new Size(80, 12),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SubtitleFont,
            BackColor = Color.Transparent
        };

        _dragGrip = new Panel
        {
            Size = new Size(36, 4),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top
        };

        HideButton = new IconButton
        {
            IconGlyph = "—",
            ToolTipText = "Minimize to tray",
            Location = new Point(Width - 40, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        Controls.Add(HideButton);
        Controls.Add(_dragGrip);
        Controls.Add(_versionLabel);
        Controls.Add(_titleLabel);
        Controls.Add(_iconBox);

        Resize += (_, _) =>
        {
            HideButton.Location = new Point(Width - 40, 10);
            _dragGrip.Location = new Point((Width - _dragGrip.Width) / 2, 6);
        };

        _dragGrip.Paint += (_, e) =>
        {
            var g = e.Graphics;
            AppTheme.ConfigureGraphics(g);
            var y = _dragGrip.Height / 2 - 1;
            using var brush = new SolidBrush(Color.FromArgb(80, AppTheme.TextMuted));
            g.FillRectangle(brush, 4, y, 28, 2);
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        AppTheme.ConfigureGraphics(g);

        var bounds = ClientRectangle;
        using var brush = new LinearGradientBrush(
            bounds,
            AppTheme.HeaderBackground,
            AppTheme.HeaderGradientEnd,
            LinearGradientMode.Vertical);
        g.FillRectangle(brush, bounds);

        using var linePen = new Pen(AppTheme.BorderSubtle);
        g.DrawLine(linePen, 0, bounds.Bottom - 1, bounds.Width, bounds.Bottom - 1);
    }
}
