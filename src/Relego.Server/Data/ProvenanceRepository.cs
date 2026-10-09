using System.Data;
using Dapper;
using Relego.Server.Models;

namespace Relego.Server.Data;

/// <summary>
/// Persists insert-only provenance for highlights observed through a cloud provider. The canonical
/// dedup stays <c>uq_highlights_user_book_text</c>; this table never replaces or deletes highlights.
/// </summary>
public sealed class ProvenanceRepository(IDbConnection connection)
{
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
        connection.ExecuteAsync(
            """
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
            """,
            record);
}
