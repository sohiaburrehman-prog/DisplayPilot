using System.Reflection;

namespace PrimaryDisplaySwap;

internal static class LegalDocuments
{
    public const string EulaTitle = "End User License Agreement";
    public const string PrivacyTitle = "Privacy Policy";
    public const string ThirdPartyNoticesTitle = "Third-Party Notices";

    public static string LoadEula() => LoadEmbedded("PrimaryDisplaySwap.docs.legal.EULA.txt");

    public static string LoadPrivacyPolicy() => LoadEmbedded("PrimaryDisplaySwap.docs.legal.PrivacyPolicy.txt");

    public static string LoadThirdPartyNotices() => LoadEmbedded("PrimaryDisplaySwap.THIRD-PARTY-NOTICES.md");

    private static string LoadEmbedded(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded legal document: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
