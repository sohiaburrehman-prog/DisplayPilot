using System.Diagnostics;

namespace PrimaryDisplaySwap;

/// <summary>Opens http(s) and mailto links safely — blocks shell execution of untrusted schemes.</summary>
internal static class UrlLaunchHelper
{
    private static readonly HashSet<string> WebSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
    };

    private static readonly HashSet<string> WebAndMailSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
    };

    public static bool IsAllowedWebUrl(string? url) => MatchesAllowedScheme(url, WebSchemes);

    public static bool IsAllowedWebOrMailUrl(string? url) => MatchesAllowedScheme(url, WebAndMailSchemes);

    public static bool TryOpenWebUrl(string? url) => TryOpen(url, WebSchemes);

    public static bool TryOpenWebOrMailUrl(string? url) => TryOpen(url, WebAndMailSchemes);

    private static bool MatchesAllowedScheme(string? url, HashSet<string> allowedSchemes)
    {
        return !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && allowedSchemes.Contains(uri.Scheme);
    }

    private static bool TryOpen(string? url, HashSet<string> allowedSchemes)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !allowedSchemes.Contains(uri.Scheme))
        {
            AppLogger.Log($"Blocked opening untrusted URL: '{url}'");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not open URL '{url}': {ex.Message}");
            return false;
        }
    }
}
