using Relego.Core.Annotations;

namespace Relego.Core.Parsing;

/// <summary>
/// Splits the reading markers a reader writes at the start of a note (<c>.h1</c>, <c>.c2</c>,
/// <c>.tag</c>) from the note prose. Matching is case-insensitive and the reader's spelling is
/// preserved. Pure: no I/O.
/// </summary>
public static class AnnotationMarkerParser
{
    /// <summary>Reads the leading markers of a note.</summary>
    /// <param name="note">Raw note text, markers included, or <c>null</c>.</param>
    /// <returns>
    /// The markers found and the remaining prose; <see cref="AnnotationMarkers.Empty"/> when the
    /// note is blank.
    /// </returns>
    public static AnnotationMarkers Parse(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return AnnotationMarkers.Empty;

        var span = note.AsSpan();
        var index = 0;
        int? headingLevel = null;
        int? stitchLevel = null;
        string? stitchMarker = null;
        var tags = new List<string>();

        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
                index++;

            if (index >= span.Length)
                break;

            var tokenStart = index;
            while (index < span.Length && !char.IsWhiteSpace(span[index]))
                index++;

            var token = span[tokenStart..index];

            // Only leading dot-tokens are markers; the first other word ends the vocabulary and
            // starts the prose.
            if (token[0] != '.')
            {
                index = tokenStart;
                break;
            }

            if (TryReadLevel(token, 'h', out var level))
            {
                // ".h0", ".h4": heading-shaped but not a valid level, so the token is prose.
                if (level is < 1 or > 3)
                {
                    index = tokenStart;
                    break;
                }

                headingLevel = level;
                continue;
            }

            if (TryReadLevel(token, 'c', out level))
            {
                // ".c0" is not a stitch level, so the token is prose.
                if (level < 1)
                {
                    index = tokenStart;
                    break;
                }

                stitchLevel = level;
                stitchMarker = token[1..].ToString();
                continue;
            }

            if (!IsTagToken(token))
            {
                // A bare "." or a token with a second dot is prose, dot included.
                index = tokenStart;
                break;
            }

            var tag = token[1..].ToString();
            if (!tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                tags.Add(tag);
        }

        var prose = note[index..].Trim();

        return new AnnotationMarkers
        {
            Note = prose.Length == 0 ? null : prose,
            HeadingLevel = headingLevel,
            StitchLevel = stitchLevel,
            StitchMarker = stitchMarker,
            Tags = tags,
        };
    }

    // Dot-tokens of the form ".h<digits>" / ".c<digits>"; the level is clamped so a hugely long
    // number stays out of range instead of throwing.
    private static bool TryReadLevel(ReadOnlySpan<char> token, char marker, out int level)
    {
        level = 0;

        if (token.Length < 3 || char.ToLowerInvariant(token[1]) != marker)
            return false;

        long value = 0;
        for (var i = 2; i < token.Length; i++)
        {
            if (!char.IsAsciiDigit(token[i]))
                return false;

            value = Math.Min((value * 10) + (token[i] - '0'), int.MaxValue);
        }

        level = (int)value;
        return true;
    }

    // A tag is a single dot followed by a word with no further dot.
    private static bool IsTagToken(ReadOnlySpan<char> token)
        => token.Length > 1 && token[1..].IndexOf('.') < 0;
}
