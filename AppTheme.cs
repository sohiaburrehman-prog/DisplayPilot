using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PrimaryDisplaySwap;

internal static class AppTheme
{
    public const string AppName = "DisplayPilot";

    // Deep navy / charcoal — Windows 11 settings-adjacent palette
    public static readonly Color Background = Color.FromArgb(15, 17, 24);
    public static readonly Color BackgroundElevated = Color.FromArgb(22, 25, 33);
    public static readonly Color HeaderBackground = Color.FromArgb(12, 14, 20);
    public static readonly Color HeaderGradientEnd = Color.FromArgb(26, 30, 40);
    public static readonly Color Surface = Color.FromArgb(28, 32, 42);
    public static readonly Color SurfaceElevated = Color.FromArgb(34, 39, 50);
    public static readonly Color SurfaceHover = Color.FromArgb(42, 48, 62);
    public static readonly Color SurfaceSelected = Color.FromArgb(36, 44, 58);
    public static readonly Color Border = Color.FromArgb(52, 60, 76);
    public static readonly Color BorderSubtle = Color.FromArgb(38, 44, 58);
    public static readonly Color BorderFocus = Color.FromArgb(56, 148, 255);

    public static readonly Color AccentPrimary = Color.FromArgb(56, 132, 255);
    public static readonly Color AccentPrimaryHover = Color.FromArgb(88, 156, 255);
    public static readonly Color AccentPrimaryGlow = Color.FromArgb(72, 56, 132, 255);
    public static readonly Color AccentSwap = Color.FromArgb(0, 178, 156);
    public static readonly Color AccentSwapHover = Color.FromArgb(42, 198, 176);
    public static readonly Color Secondary = Color.FromArgb(44, 50, 64);
    public static readonly Color SecondaryHover = Color.FromArgb(58, 66, 82);

    public static readonly Color TextPrimary = Color.FromArgb(245, 247, 252);
    public static readonly Color TextSecondary = Color.FromArgb(168, 176, 192);
    public static readonly Color TextMuted = Color.FromArgb(108, 116, 132);
    public static readonly Color Error = Color.FromArgb(255, 102, 102);
    public static readonly Color Success = Color.FromArgb(64, 210, 138);
    public static readonly Color Warning = Color.FromArgb(255, 190, 70);

    public const int Spacing = 8;
    public const int CornerRadius = 12;
    public const int ButtonCornerRadius = 8;
    public const int CardCornerRadius = 10;
    public const int MonitorCardHeight = 68;
    public const int FormWidth = 360;

    private static readonly string UiFontFamily = ResolveUiFontFamily();

    public static readonly Font TitleFont = new(UiFontFamily, 10.25F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SubtitleFont = new(UiFontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyFont = new(UiFontFamily, 9.25F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodySemiboldFont = new(UiFontFamily, 9.25F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font ButtonFont = new(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font CaptionFont = new(UiFontFamily, 8F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BadgeFont = new(UiFontFamily, 6.75F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SectionFont = new(UiFontFamily, 7.5F, FontStyle.Bold, GraphicsUnit.Point);

    // Tray context menus scale these per-monitor (PerMonitorV2) instead of using
    // the fixed 96-DPI legacy sizes above.
    private static int _menuFontDpi;
    private static Font? _menuTitleFont;
    private static Font? _menuSubtitleFont;
    private static Font? _menuBodyFont;
    private static Font? _menuBodySemiboldFont;
    private static Font? _menuButtonFont;
    private static Font? _menuCaptionFont;
    private static Font? _menuSectionFont;

    public static Font MenuTitleFont => _menuTitleFont ?? BodyFont;
    public static Font MenuSubtitleFont => _menuSubtitleFont ?? CaptionFont;
    public static Font MenuBodyFont => _menuBodyFont ?? BodyFont;
    public static Font MenuBodySemiboldFont => _menuBodySemiboldFont ?? BodySemiboldFont;
    public static Font MenuButtonFont => _menuButtonFont ?? ButtonFont;
    public static Font MenuCaptionFont => _menuCaptionFont ?? CaptionFont;
    public static Font MenuSectionFont => _menuSectionFont ?? SectionFont;

    /// <summary>Rebuilds tray-menu fonts for the monitor DPI hosting the cursor.</summary>
    public static void RefreshMenuFonts()
    {
        var dpi = GetCursorMonitorDpi();
        if (dpi == _menuFontDpi && _menuBodyFont is not null)
        {
            return;
        }

        DisposeMenuFonts();
        _menuFontDpi = dpi;
        var scale = dpi / 96f;
        _menuTitleFont = CreateScaledFont(10.25F, FontStyle.Bold, scale);
        _menuSubtitleFont = CreateScaledFont(7.5F, FontStyle.Regular, scale);
        _menuBodyFont = CreateScaledFont(9.25F, FontStyle.Regular, scale);
        _menuBodySemiboldFont = CreateScaledFont(9.25F, FontStyle.Bold, scale);
        _menuButtonFont = CreateScaledFont(9F, FontStyle.Bold, scale);
        _menuCaptionFont = CreateScaledFont(8F, FontStyle.Regular, scale);
        _menuSectionFont = CreateScaledFont(7.5F, FontStyle.Bold, scale);
    }

    private static Font CreateScaledFont(float points, FontStyle style, float scale) =>
        new(UiFontFamily, points * scale, style, GraphicsUnit.Point);

    private static int GetCursorMonitorDpi()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        var pt = new PointNative { X = pos.X, Y = pos.Y };
        var monitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 &&
            dpiX > 0)
        {
            return (int)dpiX;
        }

        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return (int)g.DpiY;
    }

    private const int MdtEffectiveDpi = 0;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PointNative pt, uint dwFlags);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private static void DisposeMenuFonts()
    {
        _menuTitleFont?.Dispose();
        _menuSubtitleFont?.Dispose();
        _menuBodyFont?.Dispose();
        _menuBodySemiboldFont?.Dispose();
        _menuButtonFont?.Dispose();
        _menuCaptionFont?.Dispose();
        _menuSectionFont?.Dispose();

        _menuTitleFont = null;
        _menuSubtitleFont = null;
        _menuBodyFont = null;
        _menuBodySemiboldFont = null;
        _menuButtonFont = null;
        _menuCaptionFont = null;
        _menuSectionFont = null;
    }

    public static string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        }
    }

    public static void ApplyDarkForm(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = TextPrimary;
        form.Font = BodyFont;
        form.Padding = new Padding(1);
    }

    public static void ConfigureGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRect(Graphics g, Rectangle bounds, int radius, Color fill)
    {
        using var path = CreateRoundedRect(bounds, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRect(Graphics g, Rectangle bounds, int radius, Color stroke, float width = 1f)
    {
        using var path = CreateRoundedRect(bounds, radius);
        using var pen = new Pen(stroke, width);
        g.DrawPath(pen, path);
    }

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static string ResolveUiFontFamily()
    {
        foreach (var family in new[] { "Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI" })
        {
            if (FontFamily.Families.Any(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)))
            {
                return family;
            }
        }

        return "Segoe UI";
    }
}

internal static class FormChromeHelper
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void ApplyBorderlessChrome(Form form)
    {
        form.FormBorderStyle = FormBorderStyle.None;
        ApplyRoundedCorners(form);
    }

    public static void EnableDragOnControl(Control dragArea, Form form)
    {
        dragArea.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(form.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        };
    }

    public static void ApplyRoundedCorners(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        void Apply()
        {
            var preference = DwmwcpRound;
            DwmSetWindowAttribute(form.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }

        if (form.IsHandleCreated)
        {
            Apply();
        }
        else
        {
            form.HandleCreated += (_, _) => Apply();
        }
    }
}
