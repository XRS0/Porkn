using System.Text.RegularExpressions;

namespace Porkn.Windows;

internal static partial class SensitiveRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var redacted = UrlUserInfoRegex().Replace(value, "$1<redacted>@");
        redacted = UuidRegex().Replace(redacted, "<uuid>");
        redacted = SecretFieldRegex().Replace(redacted, "$1\"<redacted>\"");
        redacted = TokenRegex().Replace(redacted, "$1<redacted>");
        return redacted;
    }

    [GeneratedRegex(@"([a-z][a-z0-9+.-]*://)[^\s/@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex UuidRegex();

    [GeneratedRegex("(\\\"(?:password|passwd|token|uuid|server_key|public_key|short_id)\\\"\\s*:\\s*)\\\"[^\\\"]*\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SecretFieldRegex();

    [GeneratedRegex(@"([?&](?:token|key|password|passwd)=)[^&#\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();
}
