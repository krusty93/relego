using System.Data;
using System.Globalization;
using Dapper;
using Relego.Core.Contracts;
using Relego.Server.Models;
using Relego.Server.Services;

namespace Relego.Server.Data;

public sealed class RecapRepository(IDbConnection connection)
{
    public async Task<int> CreateJobAsync(int userId, DateTimeOffset scheduledFor)
    {
        var scheduledForText = scheduledFor.UtcDateTime.ToString("O");
        var createdAtText = DateTimeOffset.UtcNow.ToString("O");

        return await connection.QuerySingleOrDefaultAsync<int>(
            """
            INSERT OR IGNORE INTO recap_jobs (user_id, scheduled_for, created_at)
            VALUES (@UserId, @ScheduledFor, @CreatedAt);
            SELECT id FROM recap_jobs WHERE user_id = @UserId AND scheduled_for = @ScheduledFor
            """,
            new { UserId = userId, ScheduledFor = scheduledForText, CreatedAt = createdAtText });
    }

    public async Task<RecapJobRecord?> GetJobBySlotAsync(int userId, DateTimeOffset scheduledFor)
    {
        var scheduledForText = scheduledFor.UtcDateTime.ToString("O");

        return await connection.QuerySingleOrDefaultAsync<RecapJobRecord>(
            """
            SELECT id AS Id, user_id AS UserId, scheduled_for AS ScheduledFor,
                   status AS Status, attempt_count AS AttemptCount,
                   error_message AS ErrorMessage, created_at AS CreatedAt,
                   delivered_at AS DeliveredAt
            FROM recap_jobs
            WHERE user_id = @UserId AND scheduled_for = @ScheduledFor
            """,
            new { UserId = userId, ScheduledFor = scheduledForText });
    }

    public Task UpdateJobDeliveredAsync(int jobId, DateTimeOffset deliveredAt, int attemptCount)
    {
        return connection.ExecuteAsync(
            """
            UPDATE recap_jobs
            SET status = 'delivered', delivered_at = @DeliveredAt, attempt_count = @AttemptCount
            WHERE id = @JobId
            """,
            new { JobId = jobId, DeliveredAt = deliveredAt.UtcDateTime.ToString("O"), AttemptCount = attemptCount });
    }

    public Task UpdateJobFailedAsync(int jobId, string errorMessage, int attemptCount)
    {
        return connection.ExecuteAsync(
            """
            UPDATE recap_jobs
            SET status = 'failed', error_message = @ErrorMessage, attempt_count = @AttemptCount
            WHERE id = @JobId
            """,
            new { JobId = jobId, ErrorMessage = errorMessage, AttemptCount = attemptCount });
    }

    public async Task<RecapJobRecord?> GetLastJobAsync(int userId)
    {
        return await connection.QuerySingleOrDefaultAsync<RecapJobRecord>(
            """
            SELECT id AS Id, user_id AS UserId, scheduled_for AS ScheduledFor,
                   status AS Status, attempt_count AS AttemptCount,
                   error_message AS ErrorMessage, created_at AS CreatedAt,
                   delivered_at AS DeliveredAt
            FROM recap_jobs
            WHERE user_id = @UserId
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { UserId = userId });
    }

    public async Task<IReadOnlyList<RecapHistoryItemDto>> GetHistoryAsync(int userId, int limit)
    {
        var rows = await connection.QueryAsync<HistoryRow>(
            """
            SELECT id AS Id, scheduled_for AS ScheduledForText, status AS Status,
                   attempt_count AS AttemptCount, error_message AS ErrorMessage,
                   created_at AS CreatedAtText, delivered_at AS DeliveredAtText
            FROM recap_jobs
            WHERE user_id = @UserId
            ORDER BY scheduled_for DESC
            LIMIT @Limit
            """,
            new { UserId = userId, Limit = limit }).ConfigureAwait(false);

        return
        [
            .. rows.Select(r => new RecapHistoryItemDto
            {
                Id = (int)r.Id,
                ScheduledFor = ParseTimestamp(r.ScheduledForText),
                Status = r.Status,
                AttemptCount = (int)r.AttemptCount,
                ErrorMessage = r.ErrorMessage,
                CreatedAt = ParseTimestamp(r.CreatedAtText),
                DeliveredAt = r.DeliveredAtText is null ? null : ParseTimestamp(r.DeliveredAtText),
            }),
        ];
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public async Task<IReadOnlyList<SelectionCandidate>> SelectCandidatesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rows = await connection.QueryAsync<SelectionCandidateRow>(new CommandDefinition(
            """
            SELECT
                h.id          AS Id,
                h.text        AS Text,
                b.title       AS BookTitle,
                a.name        AS AuthorName,
                h.weight      AS Weight,
                h.last_seen   AS LastSeenText,
                h.created_at  AS CreatedAtText
            FROM highlights h
            JOIN books b   ON b.id = h.book_id
            JOIN authors a ON a.id = b.author_id
            WHERE h.user_id  = @UserId
              AND h.excluded  = 0
              AND NOT EXISTS (
                    SELECT 1 FROM excluded_books eb
                    WHERE eb.user_id = @UserId AND eb.book_id = h.book_id)
              AND NOT EXISTS (
                    SELECT 1 FROM excluded_authors ea
                    WHERE ea.user_id = @UserId AND ea.author_id = b.author_id)
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new SelectionCandidate(
                (int)r.Id,
                r.Text,
                r.BookTitle,
                r.AuthorName,
                (int)r.Weight,
                r.LastSeenText is null ? null : DateTimeOffset.Parse(r.LastSeenText),
                DateTimeOffset.Parse(r.CreatedAtText),
                Score: 0))
            .ToList();
    }

    public Task UpdateHighlightSeenAsync(int highlightId, DateTimeOffset seenAt)
    {
        return connection.ExecuteAsync(
            """
            UPDATE highlights
            SET last_seen = @SeenAt, delivery_count = delivery_count + 1
            WHERE id = @HighlightId
            """,
            new { HighlightId = highlightId, SeenAt = seenAt.UtcDateTime.ToString("O") });
    }

    // Raw row type for Dapper mapping (SQLite stores dates as text; integers come back as long)
    private sealed class SelectionCandidateRow
    {
        public long Id { get; set; }
        public string Text { get; set; } = "";
        public string BookTitle { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public long Weight { get; set; }
        public string? LastSeenText { get; set; }
        public string CreatedAtText { get; set; } = "";
    }

    // Raw row type for the recap history projection.
    private sealed class HistoryRow
    {
        public long Id { get; set; }
        public string ScheduledForText { get; set; } = "";
        public string Status { get; set; } = "";
        public long AttemptCount { get; set; }
        public string? ErrorMessage { get; set; }
        public string CreatedAtText { get; set; } = "";
        public string? DeliveredAtText { get; set; }
    }
}