using PrimaryDisplaySwap.Models;

namespace PrimaryDisplaySwap.Services;

/// <summary>
/// Resolves monitor labels using optional user nicknames stored in settings.
/// Keys are GDI device names (\\.\DISPLAYn) which survive reboots and reconnects.
/// </summary>
public static class MonitorDisplayHelper
{
    public static string GetDisplayName(MonitorInfo monitor, AppSettings settings)
    {
        if (settings.MonitorNicknames.TryGetValue(monitor.DeviceName, out var nickname) &&
            !string.IsNullOrWhiteSpace(nickname))
        {
            return nickname.Trim();
        }

        return monitor.Name;
    }

    public static string GetNumberedName(MonitorInfo monitor, AppSettings settings) =>
        $"{monitor.Index + 1} · {GetDisplayName(monitor, settings)}";

    public static string GetTrayMenuLine(MonitorInfo monitor, AppSettings settings) =>
        monitor.IsPrimary
            ? $"✓ {GetNumberedName(monitor, settings)}  —  {monitor.SpecsLabel}  ·  Primary"
            : $"   {GetNumberedName(monitor, settings)}  —  {monitor.SpecsLabel}";

    public static string GetMapTooltip(MonitorInfo monitor, AppSettings settings)
    {
        var role = monitor.IsPrimary ? "primary" : "click to make primary";
        return $"{GetNumberedName(monitor, settings)}\n{monitor.SpecsLabel}  ·  {role}";
    }

    public static void SetNickname(SettingsService settings, MonitorInfo monitor, string? nickname)
    {
        settings.Update(s =>
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                s.MonitorNicknames.Remove(monitor.DeviceName);
            }
            else
            {
                s.MonitorNicknames[monitor.DeviceName] = nickname.Trim();
            }
        });
    }
}
