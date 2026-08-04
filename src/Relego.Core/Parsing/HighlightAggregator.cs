namespace Relego.Core.Parsing;

/// <summary>
/// Shared normalization for highlight sources. Extracted verbatim from
/// <see cref="ClippingsParser"/> so every source (Kindle, Kobo, …) emits an
/// identical <see cref="ParseResult"/> shape. Pure, no I/O.
/// </summary>
internal static class HighlightAggregator
{
    /// <summary>
    /// The prefix applied to note text so notes are indistinguishable across sources.
    /// This is the single shared constant — sources must not redefine it (it cannot drift).
    /// </summary>
    internal const string NotePrefix = "[my note] ";

    /// <summary>
    /// Filters text-less clippings, deduplicates by <c>(Title, Author, finalText)</c>
    /// keeping the first occurrence, groups by <c>(Title, Author)</c> preserving
    /// first-seen order, excludes empty books, and reports counts.
    /// </summary>
    /// <param name="clippings">Raw clippings produced by a source.</param>
    /// <param name="totalEntriesProcessed">
    /// The number of rows/entries considered (including skipped ones), supplied by
    /// each source since skipped rows are not present in <paramref name="clippings"/>.
    /// </param>
    public static ParseResult Aggregate(IReadOnlyList<RawClipping> clippings, int totalEntriesProcessed)
    {
        // Filter bookmarks (empty text after trimming means bookmark)
        var highlights = clippings.Where(c => !string.IsNullOrEmpty(c.Text)).ToList();

        // Deduplicate by (Title, Author, Text) — keep first occurrence, count removals
        var seen = new HashSet<(string, string?, string)>();
        var duplicatesRemoved = 0;
        var deduped = new List<RawClipping>(highlights.Count);

        foreach (var clip in highlights)
        {
            var key = (clip.Title, clip.Author, clip.IsNote ? NotePrefix + clip.Text : clip.Text);
            if (!seen.Add(key))
            {
                duplicatesRemoved++;
                continue;
            }

            deduped.Add(clip);
        }

        // Group by (Title, Author) — preserve first-seen order
        var bookDict = new Dictionary<(string Title, string? Author), List<ParsedHighlight>>();
        var bookOrder = new List<(string Title, string? Author)>();

        foreach (var clip in deduped)
        {
            var key = (clip.Title, clip.Author);
            var text = clip.IsNote ? NotePrefix + clip.Text : clip.Text;
            var highlight = new ParsedHighlight(text, clip.Location, clip.AddedOn);

            if (!bookDict.TryGetValue(key, out var list))
            {
                list = [];
                bookDict[key] = list;
                bookOrder.Add(key);
            }

            list.Add(highlight);
        }

        var books = bookOrder
            .Select(key => new ParsedBook(key.Title, key.Author, bookDict[key]))
            .Where(b => b.Highlights.Count > 0)
            .ToList();

        return new ParseResult(books, totalEntriesProcessed, duplicatesRemoved);
    }
}
