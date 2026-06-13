using System.Reflection;

namespace PrimaryDisplaySwap;

internal static class AppInfo
{
    public const string AppName = "DisplayPilot";

    public static string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        }
    }
}
