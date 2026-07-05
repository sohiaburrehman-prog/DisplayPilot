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
        public bool NeedsHorizontalScroll { get; init; }
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
      double minTileHeight = 16,
      double scrollEpsilon = 2)
  {
    if (monitors.Count == 0)
    {
      return new Result
      {
        Tiles = Array.Empty<MonitorTile>(),
        ContentWidth = 0,
        ContentHeight = mapHeight,
        NeedsHorizontalScroll = false,
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
    var offsetY = (mapHeight - spanY * scale) / 2;

    var tiles = BuildTiles(
        monitors,
        minX,
        minY,
        scale,
        pad,
        offsetY,
        tileGap,
        tileInset,
        minTileWidth,
        minTileHeight);

    var contentRight = tiles.Max(t => t.Left + t.Width + tileInset);
    var naturalWidth = contentRight + pad;
    var needsScroll = naturalWidth > usableWidth + scrollEpsilon;

    if (!needsScroll)
    {
      var centerShift = Math.Max(0, (usableWidth - naturalWidth) / 2);
      if (centerShift > 0)
      {
        tiles = ShiftTiles(tiles, centerShift, 0);
      }
    }

    var contentBottom = tiles.Max(t => t.Top + t.Height + tileInset);
    var contentWidth = needsScroll ? naturalWidth : usableWidth;
    var contentHeight = Math.Max(contentBottom + pad, mapHeight);

    return new Result
    {
      Tiles = tiles,
      ContentWidth = contentWidth,
      ContentHeight = contentHeight,
      NeedsHorizontalScroll = needsScroll,
    };
  }

  private static List<MonitorTile> BuildTiles(
      IReadOnlyList<MonitorInfo> monitors,
      double minX,
      double minY,
      double scale,
      double offsetX,
      double offsetY,
      double tileGap,
      double tileInset,
      double minTileWidth,
      double minTileHeight)
  {
    var tiles = new List<MonitorTile>(monitors.Count);

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
    }

    return tiles;
  }

  private static List<MonitorTile> ShiftTiles(
      IReadOnlyList<MonitorTile> tiles,
      double deltaX,
      double deltaY)
  {
    return tiles.Select(t => new MonitorTile
    {
      Monitor = t.Monitor,
      Left = t.Left + deltaX,
      Top = t.Top + deltaY,
      Width = t.Width,
      Height = t.Height,
    }).ToList();
  }
}
