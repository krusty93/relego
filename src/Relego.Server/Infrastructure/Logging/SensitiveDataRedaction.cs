using System.Text.RegularExpressions;

namespace Relego.Server.Infrastructure.Logging;

/// <summary>
/// Redacts the values that must never reach a log file: the one-time pairing token, Amazon session
/// cookies and headers, and Kindle notebook page content. The server never logs these on purpose,
/// so this is defence in depth: both log properties whose <em>name</em> marks them as secret and
/// values that merely <em>look</em> sensitive are scrubbed.
/// </summary>
internal static partial class SensitiveDataRedaction
{
    /// <summary>Replacement written in place of a redacted value.</summary>
    internal const string Redacted = "[redacted]";

    /// <summary>Replacement written in place of a page dump, together with everything after it.</summary>
    internal const string RedactedPageContent = "[redacted page content]";

    /// <summary>
    /// Property-name fragments that make a value a secret regardless of what it looks like.
    /// <c>code</c> is deliberately absent: failure codes are diagnostics we want to keep.
    /// </summary>
    private static readonly string[] SensitiveNameFragments =
    [
        "token", "pairing", "secret", "password", "passwd", "credential", "cookie",
        "authorization", "session", "apikey", "api_key"
    ];

    private static readonly (Regex Pattern, string Replacement)[] ValueRules =
        [(HeaderPattern(), "$1$2" + Redacted), (AmazonCookiePattern(), Redacted), (TokenPattern(), Redacted)];

    /// <summary>Whether a log property name marks its value as a secret.</summary>
    /// <param name="name">The property name.</param>
    /// <returns><c>true</c> when the name is sensitive.</returns>
    internal static bool IsSensitiveName(string name) =>
        SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a value looks like a secret or like page content, whatever it is called.</summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns><c>true</c> when the value must be scrubbed.</returns>
    internal static bool ContainsSensitiveValue(string value) =>
        HtmlPattern().IsMatch(value) || ValueRules.Any(rule => rule.Pattern.IsMatch(value));

    /// <summary>Scrubs every sensitive value found in <paramref name="text"/>.</summary>
    /// <param name="text">The text to scrub.</param>
    /// <returns>The scrubbed text.</returns>
    internal static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Page content is opaque and unbounded, so the tag and everything after it go: a single
        // leaked dump is worth more than the rest of the line.
        var scrubbed = HtmlPattern().Replace(text, RedactedPageContent);

        foreach (var (pattern, replacement) in ValueRules)
            scrubbed = pattern.Replace(scrubbed, replacement);

        return scrubbed;
    }

    [GeneratedRegex(@"(?i)\b(cookie|set-cookie|authorization)(\s*[=:]\s*)[^\r\n]*")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"(?i)\b(session-id|session-token|sess-at-acb|sst-acb|at-acb[a-z0-9-]*|ubid-acb[a-z0-9-]*|x-acb[a-z0-9-]*|lc-acb[a-z0-9-]*)\s*[=:]\s*[^\s;,""']+")]
    private static partial Regex AmazonCookiePattern();

    [GeneratedRegex(@"(?i)\b(pairing[-_]?token|pairing[-_]?code|token)\s*[=:]\s*[A-Za-z0-9_\-.]{8,}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?is)<!?\s*(doctype\s+html|html)\b.*")]
    private static partial Regex HtmlPattern();
}
