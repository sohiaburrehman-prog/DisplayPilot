using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PrimaryDisplaySwap.Services;

/// <summary>Result of a GitHub release check.</summary>
public sealed class UpdateInfo
{
    public bool UpdateAvailable { get; init; }
    public string LatestTag { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
}

/// <summary>
/// Telemetry-free update check: queries the public GitHub releases API for the
/// latest tag and compares it to the running version. Makes no other network
/// calls and never downloads anything — it only surfaces a link.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/sohiaburrehman-prog/DisplayPilot/releases/latest";

    public const string ReleasesPage =
        "https://github.com/sohiaburrehman-prog/DisplayPilot/releases";

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

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Log($"Update check: GitHub returned {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? string.Empty : string.Empty;
            var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? string.Empty : string.Empty;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            var isNewer = IsNewer(tag, AppInfo.AppVersion);
            AppLogger.Log($"Update check: latest tag '{tag}', running '{AppInfo.AppVersion}', newer={isNewer}.");

            return new UpdateInfo
            {
                UpdateAvailable = isNewer,
                LatestTag = tag,
                ReleaseUrl = string.IsNullOrWhiteSpace(url) ? ReleasesPage : url,
                ReleaseName = name,
            };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Compares a release tag (e.g. "v1.3.0") against the current version.</summary>
    public static bool IsNewer(string tag, string currentVersion)
    {
        if (TryParseVersion(tag, out var latest) && TryParseVersion(currentVersion, out var current))
        {
            return latest > current;
        }

        return false;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().TrimStart('v', 'V');

        // Drop any pre-release / build suffix after the numeric core.
        var core = new string(trimmed.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (string.IsNullOrEmpty(core))
        {
            return false;
        }

        return Version.TryParse(core.Contains('.') ? core : core + ".0", out version!);
    }
}
