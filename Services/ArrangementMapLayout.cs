using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>Maps Windows virtual-desktop monitor bounds to flyout arrangement-map coordinates.</summary>
internal static class ArrangementMapLayout
{
    internal sealed class MonitorTile
    {
        public required MonitorInfo Monitor { get; init; }
        public double Left { get; init; }
        public double Top { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }

    internal sealed class Result
    {
        public required IReadOnlyList<MonitorTile> Tiles { get; init; }
        public double ContentWidth { get; init; }
        public double ContentHeight { get; init; }
    }

    /// <summary>
    /// Scales monitors to the map height (preserving aspect ratio). When the scaled
    /// desktop is wider than the viewport, callers should enable horizontal scrolling
    /// instead of shrinking tiles below readable size.
    /// </summary>
    internal static Result Compute(
        IReadOnlyList<MonitorInfo> monitors,
        double viewportWidth,
        double mapHeight,
        double pad = 4,
        double tileGap = 4,
        double tileInset = 2,
        double minTileWidth = 24,
        double minTileHeight = 16)
    {
        if (monitors.Count == 0)
        {
            return new Result
            {
                Tiles = Array.Empty<MonitorTile>(),
                ContentWidth = 0,
                ContentHeight = mapHeight,
            };
        }

        var usableWidth = Math.Max(viewportWidth, 1);
        var minX = monitors.Min(m => (double)m.PositionX);
        var minY = monitors.Min(m => (double)m.PositionY);
        var maxX = monitors.Max(m => (double)m.PositionX + m.Width);
        var maxY = monitors.Max(m => (double)m.PositionY + m.Height);
        var spanX = Math.Max(maxX - minX, 1);
        var spanY = Math.Max(maxY - minY, 1);

        var scale = (mapHeight - pad * 2) / spanY;
        var scaledSpanX = spanX * scale;
        var centerWhenFits = scaledSpanX + tileGap * 2 <= usableWidth - pad * 2;
        var offsetX = centerWhenFits
            ? (usableWidth - scaledSpanX) / 2
            : pad;
        var offsetY = (mapHeight - spanY * scale) / 2;

        var tiles = new List<MonitorTile>(monitors.Count);
        var contentRight = 0d;
        var contentBottom = 0d;

        foreach (var monitor in monitors)
        {
            var width = Math.Max(monitor.Width * scale - tileGap, minTileWidth);
            var height = Math.Max(monitor.Height * scale - tileGap, minTileHeight);
            var left = offsetX + (monitor.PositionX - minX) * scale + tileInset;
            var top = offsetY + (monitor.PositionY - minY) * scale + tileInset;

            tiles.Add(new MonitorTile
            {
                Monitor = monitor,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
            });

            contentRight = Math.Max(contentRight, left + width + tileInset);
            contentBottom = Math.Max(contentBottom, top + height + tileInset);
        }

        var contentWidth = Math.Max(contentRight + pad, usableWidth);
        var contentHeight = Math.Max(contentBottom + pad, mapHeight);

        return new Result
        {
            Tiles = tiles,
            ContentWidth = contentWidth,
            ContentHeight = contentHeight,
        };
    }
}
