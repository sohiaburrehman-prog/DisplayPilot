using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrimaryDisplaySwap.Services;

/// <summary>Loads release notes from the embedded CHANGELOG or GitHub releases API.</summary>
public static class ChangelogService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DisplayPilot", AppInfo.AppVersion));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("CHANGELOG.md", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return $"No embedded changelog. See {UpdateService.ReleasesPage}.";
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return $"No embedded changelog. See {UpdateService.ReleasesPage}.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Extracts the section for a version from CHANGELOG.md (e.g. "1.5.0" or "v1.5.0").</summary>
    public static string GetSectionForVersion(string version, string? fullChangelog = null)
    {
        var changelog = fullChangelog ?? LoadEmbedded();
        var normalized = version.Trim().TrimStart('v', 'V');
        var pattern = $@"^##\s+\[?v?{Regex.Escape(normalized)}\]?[^\n]*\n(.*?)(?=^##\s|\Z)";
        var match = Regex.Match(changelog, pattern, RegexOptions.Multiline | RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return $"See the full changelog at {UpdateService.ReleasesPage}.";
    }

    public static string BuildWhatsNewTitle(string version) =>
        $"What's new in v{version.Trim().TrimStart('v', 'V')}";

    public async static Task<string?> FetchReleaseBodyAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var normalized = tag.Trim().TrimStart('v', 'V');
        var url =
            $"https://api.github.com/repos/sohiaburrehman-prog/DisplayPilot/releases/tags/v{normalized}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("body", out var bodyEl))
            {
                var body = bodyEl.GetString();
                return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Changelog fetch failed for '{tag}': {ex.Message}");
        }

        return null;
    }

    public static bool ShouldShowWhatsNew(string? lastSeenVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            return false;
        }

        if (string.Equals(
                NormalizeVersion(lastSeenVersion),
                NormalizeVersion(currentVersion),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UpdateService.IsNewer(currentVersion, lastSeenVersion);
    }

    private static string NormalizeVersion(string value) =>
        value.Trim().TrimStart('v', 'V');
}
