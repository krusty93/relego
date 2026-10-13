using System.Data;
using Dapper;
using Relego.Core.Contracts;
using Relego.Server.Models;

namespace Relego.Server.Data;

/// <summary>
/// Persists insert-only provenance for highlights observed through a cloud provider. The canonical
/// dedup stays <c>uq_highlights_user_book_text</c>; this table never replaces or deletes highlights.
/// </summary>
public sealed class ProvenanceRepository(IDbConnection connection)
{
    private const string UpsertSql = """
        INSERT INTO highlight_provenance
            (highlight_id, connection_id, source_id, region_id, external_id, truncated, location, note, color, first_seen_at, last_seen_at)
        VALUES
            (@HighlightId, @ConnectionId, @SourceId, @RegionId, @ExternalId, @Truncated, @Location, @Note, @Color, @FirstSeenAt, @LastSeenAt)
        ON CONFLICT(highlight_id) DO UPDATE SET
            connection_id = excluded.connection_id,
            region_id = excluded.region_id,
            external_id = excluded.external_id,
            truncated = excluded.truncated,
            location = excluded.location,
            note = excluded.note,
            color = excluded.color,
            last_seen_at = excluded.last_seen_at
        """;

    /// <summary>
    /// Returns the highlight previously observed for a provider external id within a region, or
    /// <c>null</c> when there is none. Inputs without an external id never match.
    /// </summary>
    public async Task<long?> FindHighlightIdAsync(string sourceId, string? regionId, string? externalId)
    {
        if (string.IsNullOrEmpty(externalId))
            return null;

        return await connection.QuerySingleOrDefaultAsync<long?>(
            """
            SELECT highlight_id
            FROM highlight_provenance
            WHERE source_id = @SourceId
              AND region_id IS @RegionId
              AND external_id = @ExternalId
            """,
            new { SourceId = sourceId, RegionId = regionId, ExternalId = externalId });
    }

    /// <summary>
    /// Inserts provenance for a highlight, or, when a row already exists for that highlight, only
    /// refreshes <c>last_seen_at</c> (the first observation is preserved).
    /// </summary>
    public Task UpsertAsync(HighlightProvenanceRecord record) =>
        connection.ExecuteAsync(UpsertSql, record);

    /// <summary>
    /// Returns the highlights already observed under a provider external id within a region, keyed
    /// by that external id. Callers use the map to treat a re-observed highlight as a duplicate
    /// before the shared text dedup runs.
    /// </summary>
    /// <param name="sourceId">Provider source id, for example <c>kindle-cloud</c>.</param>
    /// <param name="regionId">Region of the connection, or <c>null</c>.</param>
    /// <param name="externalIds">External ids to look up; null or empty ids are ignored.</param>
    /// <returns>External id to highlight id, for the ids that are already known.</returns>
    public async Task<IReadOnlyDictionary<string, long>> ListObservedAsync(
        string sourceId,
        string? regionId,
        IEnumerable<string?> externalIds)
    {
        var ids = externalIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
            return new Dictionary<string, long>(StringComparer.Ordinal);

        var rows = await connection.QueryAsync<ObservedRow>(
            """
            SELECT external_id AS ExternalId, highlight_id AS HighlightId
            FROM highlight_provenance
            WHERE source_id = @SourceId
              AND region_id IS @RegionId
              AND external_id IN @ExternalIds
            """,
            new { SourceId = sourceId, RegionId = regionId, ExternalIds = ids });

        return rows.ToDictionary(row => row.ExternalId, row => row.HighlightId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Records provenance for every highlight of a committed provider batch that exists in the
    /// library. A highlight is resolved by provider external id within the region first and by the
    /// shared book/text key second, so a re-observation whose text changed still refreshes the
    /// original row instead of being lost.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="sourceId">Provider source id, for example <c>kindle-cloud</c>.</param>
    /// <param name="regionId">Region of the connection, or <c>null</c>.</param>
    /// <param name="connectionId">Connection that observed the batch, or <c>null</c>.</param>
    /// <param name="books">The normalized provider batch that was just imported.</param>
    /// <param name="observed">External id to highlight id for highlights observed earlier.</param>
    /// <param name="observedAt">Timestamp recorded as the observation time.</param>
    /// <returns>The number of provenance rows written or refreshed.</returns>
    public async Task<int> RecordBatchAsync(
        int userId,
        string sourceId,
        string? regionId,
        long? connectionId,
        IReadOnlyList<Sync.CloudSyncBook> books,
        IReadOnlyDictionary<string, long> observed,
        string observedAt)
    {
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(observed);

        var written = 0;

        using var transaction = connection.BeginTransaction();

        foreach (var book in books)
        {
            var bookId = await connection.QuerySingleOrDefaultAsync<long?>(
                """
                SELECT b.id
                FROM books b
                JOIN authors a ON a.id = b.author_id
                WHERE b.user_id = @UserId AND b.title = @Title AND a.name = @AuthorName
                """,
                new
                {
                    UserId = userId,
                    Title = book.Title,
                    AuthorName = SyncRepository.ResolveAuthorName(book.Author)
                },
                transaction);

            if (bookId is null)
                continue;

            foreach (var highlight in book.Highlights)
            {
                if (string.IsNullOrEmpty(highlight.Text))
                    continue;

                long? highlightId = highlight.ExternalId is not null && observed.TryGetValue(highlight.ExternalId, out var known)
                    ? known
                    : await connection.QuerySingleOrDefaultAsync<long?>(
                        """
                        SELECT id
                        FROM highlights
                        WHERE user_id = @UserId AND book_id = @BookId AND text = @Text
                        """,
                        new { UserId = userId, BookId = bookId, Text = highlight.Text },
                        transaction);

                if (highlightId is null)
                    continue;

                written += await connection.ExecuteAsync(
                    UpsertSql,
                    new HighlightProvenanceRecord
                    {
                        HighlightId = highlightId.Value,
                        ConnectionId = connectionId,
                        SourceId = sourceId,
                        RegionId = regionId,
                        ExternalId = highlight.ExternalId,
                        Truncated = book.Truncated,
                        Location = highlight.Location,
                        Note = highlight.Note,
                        Color = highlight.Color,
                        FirstSeenAt = observedAt,
                        LastSeenAt = observedAt
                    },
                    transaction);
            }
        }

        transaction.Commit();
        return written;
    }

    /// <summary>
    /// Returns the fingerprint of the books already known for a connection's current region, keyed
    /// by the shared normalized book title (<see cref="Sync.SyncBookKey"/>), so the extension can
    /// skip unchanged books on routine runs. Because provenance is region-scoped, changing region
    /// discards the fingerprint. This is an optimization only: it never affects dedup.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="sourceId">Provider source id, for example <c>kindle-cloud</c>.</param>
    /// <param name="regionId">Region of the connection, or <c>null</c>.</param>
    /// <returns>One entry per known book; empty when nothing has been imported yet.</returns>
    public async Task<IReadOnlyList<SyncKnownBookDto>> ListKnownBooksAsync(int userId, string sourceId, string? regionId)
    {
        var rows = await connection.QueryAsync<KnownBookRow>(
            """
            SELECT b.title AS Title, COUNT(h.id) AS HighlightCount
            FROM highlight_provenance p
            JOIN highlights h ON h.id = p.highlight_id
            JOIN books b ON b.id = h.book_id
            WHERE h.user_id = @UserId
              AND p.source_id = @SourceId
              AND p.region_id IS @RegionId
            GROUP BY b.id
            """,
            new { UserId = userId, SourceId = sourceId, RegionId = regionId });

        return rows
            .GroupBy(row => Sync.SyncBookKey.Normalize(row.Title), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .Select(group => new SyncKnownBookDto
            {
                Key = group.Key,
                HighlightCount = group.Sum(row => row.HighlightCount)
            })
            .ToList();
    }

    private sealed class KnownBookRow
    {
        public string Title { get; set; } = string.Empty;

        public int HighlightCount { get; set; }
    }

    private sealed class ObservedRow
    {
        public string ExternalId { get; set; } = string.Empty;

        public long HighlightId { get; set; }
    }
}
