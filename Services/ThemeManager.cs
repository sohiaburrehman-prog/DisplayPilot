using System.Runtime.InteropServices;

using Microsoft.Win32;

using PrimaryDisplaySwap.Models;

// WinForms implicit usings (System.Drawing / System.Windows.Forms) make these
// names ambiguous, and the project drops the WPF implicit usings — so pin the
// WPF types explicitly.
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using LinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Runtime theming for the WPF UI. Rather than swapping whole dictionaries
/// (which would not update already-loaded styles), it mutates the colour of
/// the shared brush instances declared in Themes/Theme.xaml. Because every
/// control resolves those brushes to the same instance, changing a brush's
/// colour updates the entire UI live — including styled buttons and templates.
///
/// The accent colour always follows the Windows personalisation accent; the
/// light/dark surface palette follows the user's preference (System tracks the
/// Windows "apps" light/dark setting and updates when the user flips it).
/// </summary>
public static class ThemeManager
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorization, out bool opaqueBlend);

    private static Application? _app;
    private static ThemePreference _preference = ThemePreference.System;
    private static bool _listening;

    public static bool IsLight { get; private set; }

    /// <summary>Captures the app and does the first apply. Call once, early in
    /// OnStartup, before any window is shown.</summary>
    public static void Initialize(Application app, ThemePreference preference)
    {
        _app = app;
        _preference = preference;

        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listening = true;
        }

        Apply(preference);
    }

    public static void Apply(ThemePreference preference)
    {
        if (_app is null)
        {
            return;
        }

        _preference = preference;
        IsLight = ResolveIsLight(preference);
        var accent = ResolveAccent();
        var accentHover = Lighten(accent, IsLight ? -0.12 : 0.18);

        var p = IsLight ? LightPalette() : DarkPalette();

        SetSolid("AccentBrush", accent);
        SetSolid("AccentHoverBrush", accentHover);

        foreach (var (key, color) in p.Solids)
        {
            SetSolid(key, color);
        }

        // Flyout + monitor-screen gradients (two stops each).
        SetGradient("FlyoutBrush", p.FlyoutTop, p.FlyoutBottom);
        SetGradient("FlyoutOpaqueBrush", Opaque(p.FlyoutTop), Opaque(p.FlyoutBottom));
        SetGradient("ScreenIdleBrush", p.ScreenIdleTop, p.ScreenIdleBottom);

        // Hero + selected-screen gradients follow the live accent → violet.
        var violet = FromHex("#9D6BFF");
        SetGradient("HeroGradientBrush", accent, violet);
        SetGradient("HeroGradientHoverBrush", Lighten(accent, 0.18), Lighten(violet, 0.16));
        SetGradient("ScreenGradientBrush", WithAlpha(accent, 0xCC), WithAlpha(violet, 0xCC));

        AppLogger.Log($"Theme applied: {(IsLight ? "Light" : "Dark")} (pref={preference}), accent=#{accent.R:X2}{accent.G:X2}{accent.B:X2}.");
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Windows raises General for theme/accent changes. Only react when the
        // user is letting us follow the system.
        if (_preference == ThemePreference.System &&
            (e.Category == UserPreferenceCategory.General ||
             e.Category == UserPreferenceCategory.Color ||
             e.Category == UserPreferenceCategory.VisualStyle))
        {
            _app?.Dispatcher.BeginInvoke(() => Apply(ThemePreference.System));
        }
    }

    private static bool ResolveIsLight(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => true,
        ThemePreference.Dark => false,
        _ => WindowsUsesLightApps(),
    };

    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color ResolveAccent()
    {
        try
        {
            if (DwmGetColorizationColor(out var argb, out _) == 0)
            {
                // argb is 0xAARRGGBB; ignore alpha for a solid accent.
                var c = Color.FromRgb(
                    (byte)((argb >> 16) & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)(argb & 0xFF));

                // Guard against near-white/near-black colorization giving an
                // unusable accent; fall back to the brand blue.
                var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                if (luminance is > 0.05 and < 0.95)
                {
                    return c;
                }
            }
        }
        catch
        {
            // fall through
        }

        return FromHex("#4F8DFF");
    }

    // ─────────────────────────── palettes ───────────────────────────

    private sealed class Palette
    {
        public required (string Key, Color Color)[] Solids { get; init; }
        public required Color FlyoutTop { get; init; }
        public required Color FlyoutBottom { get; init; }
        public required Color ScreenIdleTop { get; init; }
        public required Color ScreenIdleBottom { get; init; }
    }

    private static Palette DarkPalette() => new()
    {
        FlyoutTop = FromHex("#E60C1020"),
        FlyoutBottom = FromHex("#E6171F38"),
        ScreenIdleTop = FromHex("#FF262E44"),
        ScreenIdleBottom = FromHex("#FF1C2235"),
        Solids =
        [
            ("HeaderBrush", FromHex("#590A0D1A")),
            ("CardBrush", FromHex("#B31F2536")),
            ("CardHoverBrush", FromHex("#D92B3349")),
            ("MapSurfaceBrush", FromHex("#66131829")),
            ("HairlineBrush", FromHex("#24FFFFFF")),
            ("VioletBrush", FromHex("#9D6BFF")),
            ("TextPrimaryBrush", FromHex("#F6F8FE")),
            ("TextSecondaryBrush", FromHex("#AEB7CC")),
            ("TextMutedBrush", FromHex("#7E879C")),
            ("SuccessBrush", FromHex("#46E0A0")),
            ("ErrorBrush", FromHex("#FF7373")),
        ],
    };

    private static Palette LightPalette() => new()
    {
        FlyoutTop = FromHex("#F2F7F9FC"),
        FlyoutBottom = FromHex("#F2EAEEF6"),
        ScreenIdleTop = FromHex("#FFE9ECF4"),
        ScreenIdleBottom = FromHex("#FFDDE2EC"),
        Solids =
        [
            ("HeaderBrush", FromHex("#66FFFFFF")),
            ("CardBrush", FromHex("#CCFFFFFF")),
            ("CardHoverBrush", FromHex("#FFFFFFFF")),
            ("MapSurfaceBrush", FromHex("#73E5E9F2")),
            ("HairlineBrush", FromHex("#22101828")),
            ("VioletBrush", FromHex("#7C4DE0")),
            ("TextPrimaryBrush", FromHex("#111726")),
            ("TextSecondaryBrush", FromHex("#4A5468")),
            ("TextMutedBrush", FromHex("#6B7488")),
            ("SuccessBrush", FromHex("#0F9D58")),
            ("ErrorBrush", FromHex("#D93636")),
        ],
    };

    // ─────────────────────────── helpers ───────────────────────────

    private static void SetSolid(string key, Color color)
    {
        if (_app!.Resources[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            brush.Color = color;
        }
    }

    private static void SetGradient(string key, Color start, Color end)
    {
        if (_app!.Resources[key] is LinearGradientBrush { IsFrozen: false } brush &&
            brush.GradientStops.Count >= 2)
        {
            brush.GradientStops[0].Color = start;
            brush.GradientStops[^1].Color = end;
        }
    }

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Lighten(Color c, double amount)
    {
        // amount > 0 lightens toward white, < 0 darkens toward black.
        if (amount >= 0)
        {
            return Color.FromRgb(
                (byte)(c.R + (255 - c.R) * amount),
                (byte)(c.G + (255 - c.G) * amount),
                (byte)(c.B + (255 - c.B) * amount));
        }

        var k = 1 + amount;
        return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }
}
