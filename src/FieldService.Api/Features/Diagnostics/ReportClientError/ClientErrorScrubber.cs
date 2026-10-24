using System.Text.RegularExpressions;

namespace FieldService.Api.Features.Diagnostics.ReportClientError;

public static partial class ClientErrorScrubber
{
    public const string Redacted = "[redacted]";

    public const string RedactedQuery = "?[redacted]";

    public static string Scrub(string value)
    {
        var scrubbed = JsonWebToken().Replace(value, Redacted);
        scrubbed = BearerValue().Replace(scrubbed, Redacted);
        scrubbed = WebhookSecret().Replace(scrubbed, Redacted);
        scrubbed = EmailAddress().Replace(scrubbed, Redacted);
        scrubbed = RefreshToken().Replace(scrubbed, Redacted);
        return QueryString().Replace(scrubbed, RedactedQuery);
    }

    public static string? ScrubOptional(string? value) => value is null ? null : Scrub(value);

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerValue();

    [GeneratedRegex("whsec_[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookSecret();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    [GeneratedRegex("(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex RefreshToken();

    [GeneratedRegex(@"\?[^\s)]*", RegexOptions.CultureInvariant)]
    private static partial Regex QueryString();
}
