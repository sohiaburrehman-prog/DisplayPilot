using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Controls;

internal static class MonitorDrawing
{
    public static void DrawMonitorIcon(Graphics g, Rectangle bounds, bool isPrimary, bool isSelected)
    {
        var fill = isPrimary
            ? AppTheme.AccentPrimary
            : isSelected
                ? AppTheme.AccentPrimaryGlow
                : AppTheme.Secondary;

        AppTheme.FillRoundedRect(g, bounds, 6, AppTheme.BackgroundElevated);
        AppTheme.DrawRoundedRect(g, bounds, 6, isSelected ? AppTheme.BorderFocus : AppTheme.Border);

        var screen = new Rectangle(bounds.Left + 5, bounds.Top + 5, bounds.Width - 10, bounds.Height - 12);
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, screen);
        }

        var stand = new Rectangle(bounds.Left + bounds.Width / 2 - 3, bounds.Bottom - 5, 6, 3);
        using (var brush = new SolidBrush(AppTheme.Border))
        {
            g.FillRectangle(brush, stand);
        }
    }

    public static void DrawPrimaryBadge(Graphics g, Rectangle bounds)
    {
        AppTheme.FillRoundedRect(g, bounds, 8, Color.FromArgb(48, AppTheme.AccentPrimary));
        AppTheme.DrawRoundedRect(g, bounds, 8, Color.FromArgb(120, AppTheme.AccentPrimary), 1f);
        TextRenderer.DrawText(
            g,
            "PRIMARY",
            AppTheme.BadgeFont,
            bounds,
            AppTheme.AccentPrimaryHover,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    public static void DrawCardContent(Graphics g, MonitorInfo monitor, Rectangle bounds, bool isSelected, bool isHovered)
    {
        var iconBounds = new Rectangle(bounds.Left + 12, bounds.Top + (bounds.Height - 32) / 2, 32, 32);
        MonitorDrawing.DrawMonitorIcon(g, iconBounds, monitor.IsPrimary, isSelected || isHovered);

        var textLeft = iconBounds.Right + 12;
        var badgeWidth = monitor.IsPrimary ? 62 : 0;
        var textWidth = bounds.Width - textLeft - badgeWidth - 16;

        TextRenderer.DrawText(
            g,
            monitor.Name,
            AppTheme.BodySemiboldFont,
            new Rectangle(textLeft, bounds.Top + 14, textWidth, 18),
            AppTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        var subtitle = BuildSubtitle(monitor);
        TextRenderer.DrawText(
            g,
            subtitle,
            AppTheme.CaptionFont,
            new Rectangle(textLeft, bounds.Top + 34, textWidth, 16),
            AppTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        if (monitor.IsPrimary)
        {
            var badgeBounds = new Rectangle(bounds.Right - 72, bounds.Top + (bounds.Height - 20) / 2, 60, 20);
            DrawPrimaryBadge(g, badgeBounds);
        }
    }

    public static string BuildSubtitle(MonitorInfo monitor)
    {
        if (monitor.RefreshRateHz > 0)
        {
            return $"{monitor.Width} × {monitor.Height} · {monitor.RefreshRateHz} Hz";
        }

        return $"{monitor.Width} × {monitor.Height}";
    }
}
