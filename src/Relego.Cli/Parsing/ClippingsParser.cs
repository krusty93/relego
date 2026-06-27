using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Relego.Cli.Parsing;

/// <summary>
/// Parses Kindle "My Clippings.txt" files into structured highlight data.
/// Pure, static, no side effects beyond optional logging.
/// </summary>
public static partial class ClippingsParser
{
    private const string Separator = "==========";
    private const string DateFormat = "dddd, MMMM d, yyyy h:mm:ss tt";

    [GeneratedRegex(@"^- Your (?<type>Highlight|Note|Bookmark) on (?<location>.+?) \| Added on (?<date>.+)$")]
    private static partial Regex MetadataRegex();

    /// <summary>
    /// Parses a Kindle clippings file from a file path.
    /// </summary>
    public static async Task<ParseResult> ParseAsync(string filePath, ILogger? logger = null)
    {
        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        return await ParseAsync(reader, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses Kindle clippings from a TextReader.
    /// </summary>
    public static async Task<ParseResult> ParseAsync(TextReader reader, ILogger? logger = null)
    {
        var entries = await SplitEntriesAsync(reader).ConfigureAwait(false);
        var entryIndex = 0;
        var clippings = new List<RawClipping>();

        foreach (var entryLines in entries)
        {
            entryIndex++;

            if (entryLines.Count == 0 || entryLines.All(l => string.IsNullOrWhiteSpace(l)))
            {
                continue;
            }

            var clipping = TryParseEntry(entryLines, entryIndex, logger);
            if (clipping is not null)
            {
                clippings.Add(clipping);
            }
        }

        // Deduplication and grouping are shared across sources via HighlightAggregator.
        return HighlightAggregator.Aggregate(clippings, entryIndex);
    }

    private static async Task<List<List<string>>> SplitEntriesAsync(TextReader reader)
    {
        var entries = new List<List<string>>();
        var currentEntry = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (line.TrimEnd() == Separator)
            {
                if (currentEntry.Count > 0)
                {
                    entries.Add(currentEntry);
                    currentEntry = [];
                }

                continue;
            }

            currentEntry.Add(line);
        }

        // Handle last entry if file doesn't end with separator
        if (currentEntry.Count > 0)
        {
            entries.Add(currentEntry);
        }

        return entries;
    }

    private static RawClipping? TryParseEntry(List<string> lines, int entryIndex, ILogger? logger)
    {
        if (lines.Count < 2)
        {
            logger?.LogWarning("Entry {EntryIndex}: Skipped — fewer than 2 lines", entryIndex);
            return null;
        }

        var titleLine = lines[0].Trim();
        var metadataLine = lines[1].Trim();

        var (title, author) = ExtractTitleAndAuthor(titleLine);
        var metadataMatch = MetadataRegex().Match(metadataLine);

        if (!metadataMatch.Success)
        {
            logger?.LogWarning(
                "Entry {EntryIndex}: Skipped — unrecognized metadata line: {Excerpt}",
                entryIndex,
                metadataLine.Length > 200 ? metadataLine[..200] : metadataLine);
            return null;
        }

        var type = metadataMatch.Groups["type"].Value;
        var location = metadataMatch.Groups["location"].Value;
        var dateStr = metadataMatch.Groups["date"].Value;

        var isNote = type == "Note";
        var isBookmark = type == "Bookmark";

        DateTimeOffset? addedOn = DateTimeOffset.TryParseExact(
            dateStr, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

        // Extract content: lines after title, metadata, blank line
        var contentStartIndex = lines.Count > 2 && string.IsNullOrWhiteSpace(lines[2]) ? 3 : 2;
        var contentLines = lines.Skip(contentStartIndex).ToList();
        var text = string.Join("\n", contentLines).Trim();

        // Bookmarks have no text — return with empty text so caller can filter
        if (isBookmark)
        {
            return new RawClipping(title, author, IsNote: false, location, addedOn, Text: string.Empty);
        }

        return new RawClipping(title, author, isNote, location, addedOn, text);
    }

    private static (string Title, string? Author) ExtractTitleAndAuthor(string titleLine)
    {
        var lastOpenParen = titleLine.LastIndexOf('(');
        var lastCloseParen = titleLine.LastIndexOf(')');

        if (lastOpenParen < 0 || lastCloseParen < 0 || lastCloseParen <= lastOpenParen)
        {
            return (titleLine.Trim(), null);
        }

        var author = titleLine[(lastOpenParen + 1)..lastCloseParen].Trim();
        var title = titleLine[..lastOpenParen].Trim();

        if (string.IsNullOrWhiteSpace(author))
        {
            return (title, null);
        }

        return (title, author);
    }
}
