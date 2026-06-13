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

    public static Font TitleFont => new(UiFontFamily, 10.25F, FontStyle.Bold, GraphicsUnit.Point);
    public static Font SubtitleFont => new(UiFontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static Font BodyFont => new(UiFontFamily, 9.25F, FontStyle.Regular, GraphicsUnit.Point);
    public static Font BodySemiboldFont => new(UiFontFamily, 9.25F, FontStyle.Bold, GraphicsUnit.Point);
    public static Font ButtonFont => new(UiFontFamily, 9F, FontStyle.Bold, GraphicsUnit.Point);
    public static Font CaptionFont => new(UiFontFamily, 8F, FontStyle.Regular, GraphicsUnit.Point);
    public static Font BadgeFont => new(UiFontFamily, 6.75F, FontStyle.Bold, GraphicsUnit.Point);
    public static Font SectionFont => new(UiFontFamily, 7.5F, FontStyle.Bold, GraphicsUnit.Point);

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
