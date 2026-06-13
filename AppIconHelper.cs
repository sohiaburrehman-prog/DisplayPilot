using System.Reflection;

namespace PrimaryDisplaySwap;

internal static class AppIconHelper
{
    public static Icon LoadAppIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var resourceStream = assembly.GetManifestResourceStream("PrimaryDisplaySwap.Assets.AppIcon.ico");
        if (resourceStream != null)
        {
            AppLogger.Log("Loaded embedded AppIcon.ico resource.");
            using var multi = new Icon(resourceStream);
            return (Icon)multi.Clone();
        }

        var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (exeIcon != null)
        {
            AppLogger.Log("Loaded icon from executable.");
            return exeIcon;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppLogger.Log($"Loaded icon from file: {iconPath}");
            return new Icon(iconPath);
        }

        AppLogger.Log("Falling back to SystemIcons.Application.");
        return (Icon)SystemIcons.Application.Clone();
    }

    public static Icon LoadTrayIcon()
    {
        using var source = LoadAppIcon();
        var trayIcon = TryExtractIconSize(source, 16) ?? new Icon(source, 16, 16);
        AppLogger.Log($"Loaded 16x16 tray icon (handle=0x{trayIcon.Handle.ToInt64():X}).");
        return trayIcon;
    }

    private static Icon? TryExtractIconSize(Icon source, int size)
    {
        try
        {
            return new Icon(source, size, size);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not extract {size}x{size} icon layer: {ex.Message}");
            return null;
        }
    }

    public static Image LoadAppImage(int size)
    {
        using var icon = LoadAppIcon();
        using var bitmap = icon.ToBitmap();
        return new Bitmap(bitmap.GetThumbnailImage(size, size, null, IntPtr.Zero));
    }
}
