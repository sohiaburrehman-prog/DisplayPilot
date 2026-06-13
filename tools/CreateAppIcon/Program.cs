using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

static Color StrokeColor() => Color.FromArgb(18, 24, 38);
static Color ScreenPrimaryColor() => Color.FromArgb(245, 248, 255);
static Color ScreenSecondaryColor() => Color.FromArgb(176, 188, 208);
static Color AccentColor() => Color.FromArgb(255, 149, 0);
static Color AccentDarkColor() => Color.FromArgb(204, 102, 0);

static Bitmap CreateMonitorSwapIcon(int size)
{
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = size <= 16 ? SmoothingMode.None : SmoothingMode.AntiAlias;
    graphics.PixelOffsetMode = size <= 16 ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.Clear(Color.Transparent);

    if (size == 16)
    {
        DrawTrayIcon16(graphics);
        return bitmap;
    }

    var scale = size / 32f;
    var stroke = Math.Max(1f, 1.75f * scale);
    var screenH = size <= 16 ? 8f * scale : 9f * scale;
    var screenW = size <= 16 ? 6.5f * scale : 8.5f * scale;
    var topY = size <= 16 ? 4f * scale : 5f * scale;

    var leftX = size <= 16 ? 1.5f * scale : 2.5f * scale;
    var rightX = size - screenW - (size <= 16 ? 1.5f * scale : 2.5f * scale);

    DrawMonitorScreen(graphics, leftX, topY, screenW, screenH, ScreenPrimaryColor(), stroke, scale, primary: true);
    DrawMonitorScreen(graphics, rightX, topY, screenW, screenH, ScreenSecondaryColor(), stroke, scale, primary: false);

    if (size >= 32)
    {
        DrawMonitorStand(graphics, leftX, topY, screenW, screenH, stroke, scale);
        DrawMonitorStand(graphics, rightX, topY, screenW, screenH, stroke, scale);
    }

    DrawSwapArrows(graphics, size, topY, screenH, stroke, scale);

    return bitmap;
}

static void DrawTrayIcon16(Graphics graphics)
{
    DrawFilledRect(graphics, 1, 3, 5, 7, ScreenPrimaryColor());
    DrawOutlinedRect(graphics, 1, 3, 5, 7, StrokeColor());
    DrawFilledRect(graphics, 10, 3, 5, 7, ScreenSecondaryColor());
    DrawOutlinedRect(graphics, 10, 3, 5, 7, StrokeColor());

    using (var badge = new SolidBrush(AccentColor()))
    {
        graphics.FillRectangle(badge, 2, 4, 2, 2);
    }

    using (var arrow = new SolidBrush(AccentColor()))
    {
        graphics.FillRectangle(arrow, 7, 4, 2, 1);
        graphics.FillRectangle(arrow, 7, 7, 2, 1);
        graphics.FillRectangle(arrow, 6, 5, 1, 2);
        graphics.FillRectangle(arrow, 9, 5, 1, 2);
        graphics.FillRectangle(arrow, 7, 5, 2, 2);
    }
}

static void DrawFilledRect(Graphics graphics, int x, int y, int width, int height, Color fill)
{
    using var brush = new SolidBrush(fill);
    graphics.FillRectangle(brush, x, y, width, height);
}

static void DrawOutlinedRect(Graphics graphics, int x, int y, int width, int height, Color stroke)
{
    using var pen = new Pen(stroke, 1f);
    graphics.DrawRectangle(pen, x, y, width - 1, height - 1);
}

static void DrawMonitorScreen(
    Graphics graphics,
    float x,
    float y,
    float width,
    float height,
    Color fill,
    float strokeWidth,
    float scale,
    bool primary)
{
    var rect = new RectangleF(x, y, width, height);
    using var fillBrush = new SolidBrush(fill);
    using var strokePen = new Pen(StrokeColor(), strokeWidth);

    if (scale >= 0.95f)
    {
        graphics.FillRectangle(fillBrush, rect);
        graphics.DrawRectangle(strokePen, rect.X, rect.Y, rect.Width, rect.Height);
    }
    else
    {
        var pixelRect = Rectangle.Round(rect);
        graphics.FillRectangle(fillBrush, pixelRect);
        graphics.DrawRectangle(strokePen, pixelRect);
    }

    if (primary)
    {
        DrawPrimaryBadge(graphics, x, y, width, height, scale);
    }
}

static void DrawPrimaryBadge(Graphics graphics, float x, float y, float width, float height, float scale)
{
    if (scale < 0.55f)
    {
        var dot = Math.Max(2, (int)Math.Round(2f * scale));
        var dotRect = new Rectangle(
            (int)Math.Round(x + 1f * scale),
            (int)Math.Round(y + 1f * scale),
            dot,
            dot);
        using var brush = new SolidBrush(AccentColor());
        graphics.FillRectangle(brush, dotRect);
        return;
    }

    var badgeSize = Math.Max(3f, 4.5f * scale);
    var badgeX = x + width - badgeSize - Math.Max(0.5f, 1f * scale);
    var badgeY = y + Math.Max(0.5f, 1f * scale);
    DrawStar(graphics, badgeX + badgeSize / 2f, badgeY + badgeSize / 2f, badgeSize * 0.55f);
}

static void DrawStar(Graphics graphics, float centerX, float centerY, float radius)
{
    var points = new PointF[4];
    for (var i = 0; i < 4; i++)
    {
        var angle = i * Math.PI / 2 + Math.PI / 4;
        points[i] = new PointF(
            centerX + (float)Math.Cos(angle) * radius,
            centerY + (float)Math.Sin(angle) * radius);
    }

    using var brush = new SolidBrush(AccentColor());
    using var outline = new Pen(AccentDarkColor(), Math.Max(1f, radius * 0.35f));
    graphics.FillPolygon(brush, points);
    graphics.DrawPolygon(outline, points);
}

static void DrawMonitorStand(Graphics graphics, float x, float y, float width, float height, float strokeWidth, float scale)
{
    using var brush = new SolidBrush(StrokeColor());
    var neckW = Math.Max(2f, 2.5f * scale);
    var neckH = Math.Max(1.5f, 2f * scale);
    var neckX = x + (width - neckW) / 2f;
    var neckY = y + height + Math.Max(0.5f, 0.75f * scale);
    graphics.FillRectangle(brush, neckX, neckY, neckW, neckH);

    var baseW = width * 0.72f;
    var baseH = Math.Max(1.5f, 2f * scale);
    var baseX = x + (width - baseW) / 2f;
    var baseY = neckY + neckH;
    graphics.FillRectangle(brush, baseX, baseY, baseW, baseH);
}

static void DrawSwapArrows(Graphics graphics, int size, float topY, float screenH, float strokeWidth, float scale)
{
    var centerY = topY + screenH / 2f;
    var arrowColor = AccentColor();
    var arrowStroke = Math.Max(1.2f, 2.2f * scale);

    if (size <= 16)
    {
        DrawPixelSwapChevrons(graphics, size, (int)Math.Round(centerY), arrowColor);
        return;
    }

    var shaftLen = Math.Max(2.5f, 3.5f * scale);
    var headSize = Math.Max(2f, 3f * scale);
    var centerX = size / 2f;
    var leftTip = centerX - Math.Max(1.5f, 2f * scale);
    var rightTip = centerX + Math.Max(1.5f, 2f * scale);

    using var pen = new Pen(arrowColor, arrowStroke)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
    };
    using var brush = new SolidBrush(arrowColor);

    graphics.DrawLine(pen, centerX - shaftLen, centerY, leftTip, centerY);
    graphics.DrawLine(pen, centerX + shaftLen, centerY, rightTip, centerY);
    DrawTriangleHead(graphics, brush, leftTip, centerY, -1, headSize);
    DrawTriangleHead(graphics, brush, rightTip, centerY, 1, headSize);

    if (size >= 24)
    {
        var barHeight = Math.Max(1.5f, 2f * scale);
        var barWidth = Math.Max(3f, 4f * scale);
        var barRect = new RectangleF(centerX - barWidth / 2f, centerY - barHeight / 2f, barWidth, barHeight);
        using var barBrush = new SolidBrush(Color.FromArgb(120, AccentColor()));
        graphics.FillRectangle(barBrush, barRect);
    }
}

static void DrawPixelSwapChevrons(Graphics graphics, int size, int centerY, Color color)
{
    using var brush = new SolidBrush(color);
    var midX = size / 2;

    graphics.FillRectangle(brush, midX - 2, centerY - 1, 1, 3);
    graphics.FillRectangle(brush, midX - 3, centerY, 1, 1);

    graphics.FillRectangle(brush, midX + 1, centerY - 1, 1, 3);
    graphics.FillRectangle(brush, midX + 2, centerY, 1, 1);

    graphics.FillRectangle(brush, midX - 1, centerY, 2, 1);
}

static void DrawTriangleHead(Graphics graphics, Brush brush, float tipX, float tipY, int direction, float size)
{
    var points = direction < 0
        ? new[]
        {
            new PointF(tipX, tipY),
            new PointF(tipX + size, tipY - size * 0.65f),
            new PointF(tipX + size, tipY + size * 0.65f)
        }
        : new[]
        {
            new PointF(tipX, tipY),
            new PointF(tipX - size, tipY - size * 0.65f),
            new PointF(tipX - size, tipY + size * 0.65f)
        };

    graphics.FillPolygon(brush, points);
}

static byte[] BitmapToPngBytes(Bitmap bitmap)
{
    using var ms = new MemoryStream();
    bitmap.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static void WriteMultiSizeIco(string path, params int[] sizes)
{
    var images = new List<(int Size, byte[] Png)>();
    foreach (var size in sizes.OrderBy(s => s))
    {
        using var bitmap = CreateMonitorSwapIcon(size);
        images.Add((size, BitmapToPngBytes(bitmap)));
    }

    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);

    var offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
    {
        writer.Write(png);
    }
}

var iconPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets", "AppIcon.ico"));

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(iconPath))!);

WriteMultiSizeIco(iconPath, 16, 24, 32, 48);

using var verify = new Icon(iconPath);
Console.WriteLine($"Created {Path.GetFullPath(iconPath)} ({new FileInfo(iconPath).Length} bytes)");
Console.WriteLine($"Default icon size: {verify.Size}");
Console.WriteLine("Included layers: 16, 24, 32, 48");
