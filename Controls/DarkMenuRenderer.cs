namespace PrimaryDisplaySwap.Controls;

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? e.Item.Selected ? AppTheme.TextPrimary : AppTheme.TextSecondary
            : AppTheme.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        var y = bounds.Height / 2;
        using var pen = new Pen(AppTheme.BorderSubtle);
        e.Graphics.DrawLine(pen, 12, y, bounds.Width - 12, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = AppTheme.TextMuted;
        base.OnRenderArrow(e);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => AppTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => AppTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => AppTheme.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => AppTheme.Surface;
        public override Color MenuItemPressedGradientEnd => AppTheme.Surface;
        public override Color ToolStripDropDownBackground => AppTheme.SurfaceElevated;
        public override Color ImageMarginGradientBegin => AppTheme.SurfaceElevated;
        public override Color ImageMarginGradientMiddle => AppTheme.SurfaceElevated;
        public override Color ImageMarginGradientEnd => AppTheme.SurfaceElevated;
        public override Color SeparatorDark => AppTheme.BorderSubtle;
        public override Color SeparatorLight => AppTheme.BorderSubtle;
        public override Color CheckBackground => AppTheme.AccentPrimary;
        public override Color CheckPressedBackground => AppTheme.AccentPrimaryHover;
        public override Color CheckSelectedBackground => AppTheme.AccentPrimary;
    }
}
