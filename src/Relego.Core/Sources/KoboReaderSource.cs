using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Relego.Core.Parsing;

namespace Relego.Core.Sources;

/// <summary>
/// Reads highlights and notes from a Kobo <c>KoboReader.sqlite</c> database and
/// normalizes them into the same <see cref="ParseResult"/> the Kindle parser
/// produces, so everything downstream is source-agnostic (FR-010).
/// </summary>
/// <remarks>
/// The device file is never opened in place: it is copied to a temp file, opened
/// read-only, and the copy is deleted in a <c>finally</c> block, leaving the
/// on-device database byte-identical (FR-007). Owns the <c>KoboReader.sqlite</c>
/// detection rules (filename, <c>.kobo/</c> directory, SQLite-header sniff) and the
/// <see cref="KoboDetector"/> device probe.
/// </remarks>
public sealed class KoboReaderSource : IHighlightSource
{
    // "SQLite format 3\0" — the 16-byte magic header every SQLite database starts with.
    private static readonly byte[] SqliteHeader = "SQLite format 3\u0000"u8.ToArray();

    private const string DatabaseFileName = "KoboReader.sqlite";

    private const string ReadQuery =
        "SELECT c.Title, c.Attribution, b.Text, b.Annotation, b.Type, b.DateCreated, b.Hidden " +
        "FROM Bookmark b JOIN content c ON b.VolumeID = c.ContentID " +
        "ORDER BY c.Title, b.DateCreated;";

    /// <inheritdoc />
    public SourceDescriptor Descriptor { get; } = new("kobo", "Kobo");

    /// <inheritdoc />
    public SourceProbe Locate(string? userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
        {
            var detected = KoboDetector.DetectDatabasePath();
            var suggested = KoboDetector.GetSuggestedDatabasePath() ?? DatabaseFileName;
            return new SourceProbe(detected, [suggested]);
        }

        userPath = userPath.Trim();

        // Explicit file named "KoboReader.sqlite".
        if (IsDatabaseFileName(userPath))
        {
            return new SourceProbe(File.Exists(userPath) ? userPath : null, [userPath]);
        }

        // Directory (a mounted device root): the database lives under .kobo/.
        if (Directory.Exists(userPath))
        {
            var dbPath = Path.Combine(userPath, ".kobo", DatabaseFileName);
            return new SourceProbe(File.Exists(dbPath) ? dbPath : null, [dbPath]);
        }

        // An explicit, oddly-named file: sniff the SQLite header so a renamed copy still routes.
        if (File.Exists(userPath) && HasSqliteHeader(userPath))
        {
            return new SourceProbe(userPath, [userPath]);
        }

        return new SourceProbe(null, [userPath]);
    }

    /// <inheritdoc />
    public async Task<ParseResult> ReadAsync(
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Kobo database not found at '{path}'.", path);
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "relego-kobo-" + Guid.NewGuid().ToString("N") + ".sqlite");

        try
        {
            try
            {
                File.Copy(path, tempPath, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Failed to copy the Kobo database from '{path}'. " +
                    "Ensure the device is connected and the file is readable.",
                    ex);
            }

            ValidateSqliteHeader(tempPath, path);

            var (rows, totalBookmarks, orphanedCount) =
                await ReadDatabaseAsync(tempPath, path, cancellationToken).ConfigureAwait(false);

            if (orphanedCount > 0)
            {
                logger?.LogWarning(
                    "Skipped {OrphanedCount} orphaned Kobo bookmark(s) with no matching book entry.",
                    orphanedCount);
            }

            var clippings = Classify(rows, logger);

            return HighlightAggregator.Aggregate(clippings, totalBookmarks);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void ValidateSqliteHeader(string tempPath, string originalPath)
    {
        if (!HasSqliteHeader(tempPath))
        {
            throw new InvalidDataException(
                $"The file at '{originalPath}' is not a valid Kobo database (missing SQLite header).");
        }
    }

    private static bool HasSqliteHeader(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[16];
            using var stream = File.OpenRead(path);
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            return read >= header.Length && header.SequenceEqual(SqliteHeader);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsDatabaseFileName(string path)
        => string.Equals(Path.GetFileName(path), DatabaseFileName, StringComparison.OrdinalIgnoreCase);

    private static async Task<(List<KoboBookmarkRow> Rows, int TotalBookmarks, int OrphanedCount)> ReadDatabaseAsync(
        string tempPath,
        string originalPath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = tempPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        EnsureTableExists(connection, "Bookmark", originalPath);
        EnsureTableExists(connection, "content", originalPath);

        var totalBookmarks = await CountAsync(connection, "SELECT COUNT(*) FROM Bookmark;", cancellationToken)
            .ConfigureAwait(false);
        var orphanedCount = await CountAsync(
            connection,
            "SELECT COUNT(*) FROM Bookmark b LEFT JOIN content c ON b.VolumeID = c.ContentID WHERE c.ContentID IS NULL;",
            cancellationToken).ConfigureAwait(false);

        var rows = new List<KoboBookmarkRow>(totalBookmarks);

        await using var command = connection.CreateCommand();
        command.CommandText = ReadQuery;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new KoboBookmarkRow(
                Title: reader.GetString(0),
                Author: reader.IsDBNull(1) ? null : reader.GetString(1),
                Text: reader.IsDBNull(2) ? null : reader.GetString(2),
                Annotation: reader.IsDBNull(3) ? null : reader.GetString(3),
                Type: reader.IsDBNull(4) ? null : reader.GetString(4),
                DateCreated: reader.IsDBNull(5) ? null : reader.GetString(5),
                Hidden: reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString()));
        }

        return (rows, totalBookmarks, orphanedCount);
    }

    private static List<RawClipping> Classify(IReadOnlyList<KoboBookmarkRow> rows, ILogger? logger)
    {
        var clippings = new List<RawClipping>(rows.Count);
        var skipped = 0;

        foreach (var row in rows)
        {
            if (IsTruthy(row.Hidden))
            {
                skipped++;
                continue;
            }

            if (string.Equals(row.Type, "dogear", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var hasText = !string.IsNullOrEmpty(row.Text);
            var hasAnnotation = !string.IsNullOrEmpty(row.Annotation);
            if (!hasText && !hasAnnotation)
            {
                skipped++;
                continue;
            }

            var isNote = string.Equals(row.Type, "note", StringComparison.OrdinalIgnoreCase);
            string text;
            if (isNote)
            {
                // The user's typed comment (Annotation) is the note; fall back to the anchored
                // passage (Text) when the annotation is empty. The "[my note] " prefix is applied
                // by HighlightAggregator (same as the Kindle path) so notes are indistinguishable.
                text = hasAnnotation ? row.Annotation! : row.Text!;
            }
            else
            {
                if (!hasText)
                {
                    skipped++;
                    continue;
                }

                text = row.Text!;
            }

            clippings.Add(new RawClipping(
                Title: row.Title,
                Author: NormalizeAuthor(row.Author),
                IsNote: isNote,
                Location: null,
                AddedOn: ParseDate(row.DateCreated),
                Text: text));
        }

        if (skipped > 0)
        {
            logger?.LogInformation(
                "Skipped {SkippedCount} Kobo bookmark(s) (hidden, dogear, or text-less).",
                skipped);
        }

        return clippings;
    }

    private static void EnsureTableExists(SqliteConnection connection, string table, string originalPath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        if (command.ExecuteScalar() is null)
        {
            throw new InvalidDataException(
                $"The Kobo database at '{originalPath}' is missing the required '{table}' table.");
        }
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static bool IsTruthy(string? value)
        => value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

    private static string? NormalizeAuthor(string? author)
        => string.IsNullOrWhiteSpace(author) ? null : author;

    private static DateTimeOffset? ParseDate(string? raw)
        => DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp file is harmless.
        }
    }
}
