using System.Text.Json;
using Relego.Core.Contracts;

namespace Relego.Server.Sync;

/// <summary>
/// Maps stable sync failure codes to the plain-language detail and ordered recovery steps shown to
/// the user, and validates that failure diagnostics stay structural: identifiers and counts only,
/// never free text copied from the Kindle notebook page.
/// </summary>
public static class SyncFailureRecovery
{
    private const int MaxProfileVersionLength = 32;
    private const int MaxCheckLength = 128;
    private const int MaxCountKeyLength = 64;

    private static readonly Dictionary<string, (string Detail, string[] Steps)> Known = new(StringComparer.Ordinal)
    {
        ["auth_expired"] = (
            "Amazon asked for a sign-in, so the notebook could not be read.",
            [
                "Open the Kindle notebook in this browser and sign in on Amazon.",
                "Sync resumes automatically on the next cycle; nothing already imported is affected."
            ]),
        ["provider_contract_changed"] = (
            "The Kindle notebook page no longer matches the parse profile.",
            [
                "Update the Relego browser extension.",
                "Meanwhile, import locally from My Clippings.txt or your Kobo.",
                "Report the diagnostics so the parse profile can be updated."
            ]),
        ["network"] = (
            "Amazon or the Relego server could not be reached.",
            [
                "Check your connectivity and that the Relego server is running.",
                "The next cycle retries; retries never duplicate highlights."
            ]),
        ["server_rejected"] = (
            "The Relego server rejected a batch.",
            [
                "Check the server version and logs.",
                "Retry, or run a full resync."
            ]),
        ["interrupted"] = (
            "The sync stopped before it finished.",
            [
                "Keep the browser open while a sync runs.",
                "Retry when ready; retries are idempotent."
            ])
    };

    /// <summary>Returns the ordered recovery steps for a failure code, or an empty list.</summary>
    /// <param name="code">Stable failure code, or <c>null</c>.</param>
    /// <returns>The recovery steps shown to the user.</returns>
    public static IReadOnlyList<string> StepsFor(string? code) =>
        code is not null && Known.TryGetValue(code, out var entry) ? entry.Steps : [];

    /// <summary>Returns the plain-language detail for a failure code, or <c>null</c>.</summary>
    /// <param name="code">Stable failure code, or <c>null</c>.</param>
    /// <returns>The sanitized detail.</returns>
    public static string? DetailFor(string? code) =>
        code is not null && Known.TryGetValue(code, out var entry) ? entry.Detail : null;

    /// <summary>
    /// Reads the recovery steps stored as a JSON array, falling back to the code's canonical steps
    /// when the stored value is missing or unreadable.
    /// </summary>
    /// <param name="json">Stored JSON array, or <c>null</c>.</param>
    /// <param name="code">Stable failure code, or <c>null</c>.</param>
    /// <returns>The recovery steps to display.</returns>
    public static IReadOnlyList<string> ParseSteps(string? json, string? code)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var steps = JsonSerializer.Deserialize<List<string>>(json);
                if (steps is { Count: > 0 })
                    return steps;
            }
            catch (JsonException)
            {
                // Fall back to the code's canonical steps.
            }
        }

        return StepsFor(code);
    }

    /// <summary>
    /// Validates failure diagnostics, returning an actionable error when a field could carry free
    /// text from the page. Only <c>profileVersion</c>, <c>check</c> and integer <c>counts</c> with
    /// short identifier keys are accepted.
    /// </summary>
    /// <param name="diagnostics">The reported diagnostics, or <c>null</c>.</param>
    /// <returns>An error message, or <c>null</c> when the diagnostics are acceptable.</returns>
    public static string? ValidateDiagnostics(SyncFailureDiagnostics? diagnostics)
    {
        if (diagnostics is null)
            return null;

        if (!IsIdentifier(diagnostics.ProfileVersion, MaxProfileVersionLength))
            return $"diagnostics.profileVersion must be a short identifier of at most {MaxProfileVersionLength} characters with no page text.";

        if (!IsIdentifier(diagnostics.Check, MaxCheckLength))
            return $"diagnostics.check must be a short identifier of at most {MaxCheckLength} characters with no page text.";

        foreach (var entry in diagnostics.Counts)
        {
            if (!IsIdentifier(entry.Key, MaxCountKeyLength))
                return $"diagnostics.counts keys must be short identifiers of at most {MaxCountKeyLength} characters with no page text.";

            if (entry.Value < 0)
                return "diagnostics.counts values must not be negative.";
        }

        return null;
    }

    private static bool IsIdentifier(string? value, int maxLength)
    {
        if (value is null)
            return true;

        if (value.Length == 0 || value.Length > maxLength)
            return false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or '#' or '[' or ']' or '>' or ':')
                continue;

            return false;
        }

        return true;
    }
}
