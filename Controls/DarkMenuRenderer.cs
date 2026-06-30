namespace PrimaryDisplaySwap.Controls;

internal static class TrayMenuTags
{
    public const string Title = "title";
    public const string Subtitle = "subtitle";
    public const string Section = "section";
    public const string Status = "status";
    public const string Swap = "swap";
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (IsDecorativeItem(e.Item))
        {
            return;
        }

        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Tag is string tag)
        {
            e.TextColor = tag switch
            {
                TrayMenuTags.Title => AppTheme.TextPrimary,
                TrayMenuTags.Subtitle => AppTheme.TextMuted,
                TrayMenuTags.Section => AppTheme.TextMuted,
                TrayMenuTags.Status => AppTheme.TextSecondary,
                TrayMenuTags.Swap => AppTheme.AccentSwap,
                _ => e.TextColor,
            };

            e.TextFont = tag switch
            {
                TrayMenuTags.Section => AppTheme.MenuSectionFont,
                TrayMenuTags.Title => AppTheme.MenuTitleFont,
                TrayMenuTags.Subtitle => AppTheme.MenuCaptionFont,
                TrayMenuTags.Status => AppTheme.MenuCaptionFont,
                TrayMenuTags.Swap => AppTheme.MenuButtonFont,
                _ => e.TextFont,
            };
        }
        else
        {
            e.TextColor = e.Item.Enabled
                ? e.Item.Selected ? AppTheme.TextPrimary : AppTheme.TextSecondary
                : e.Item.Text?.StartsWith('✓') == true ? AppTheme.Success : AppTheme.TextMuted;
            e.TextFont = AppTheme.MenuBodyFont;
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        var y = bounds.Height / 2;
        using var pen = new Pen(AppTheme.BorderSubtle);
        e.Graphics.DrawLine(pen, 14, y, bounds.Width - 14, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = AppTheme.TextSecondary;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(AppTheme.Border);
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        bounds.Width--;
        bounds.Height--;
        e.Graphics.DrawRectangle(pen, bounds);
    }

    private static bool IsDecorativeItem(ToolStripItem item)
    {
        return item.Tag as string is TrayMenuTags.Title or TrayMenuTags.Subtitle
            or TrayMenuTags.Section or TrayMenuTags.Status;
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => AppTheme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => AppTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => AppTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => AppTheme.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => AppTheme.AccentPrimary;
        public override Color MenuItemPressedGradientEnd => AppTheme.AccentPrimary;
        public override Color ToolStripDropDownBackground => AppTheme.BackgroundElevated;
        public override Color ImageMarginGradientBegin => AppTheme.BackgroundElevated;
        public override Color ImageMarginGradientMiddle => AppTheme.BackgroundElevated;
        public override Color ImageMarginGradientEnd => AppTheme.BackgroundElevated;
        public override Color SeparatorDark => AppTheme.BorderSubtle;
        public override Color SeparatorLight => AppTheme.BorderSubtle;
        public override Color CheckBackground => AppTheme.AccentPrimary;
        public override Color CheckPressedBackground => AppTheme.AccentPrimaryHover;
        public override Color CheckSelectedBackground => AppTheme.AccentPrimary;
    }
}
