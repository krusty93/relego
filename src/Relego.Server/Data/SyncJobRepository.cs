using System.Data;
using Dapper;
using Relego.Server.Models;

namespace Relego.Server.Data;

/// <summary>
/// Persists sync jobs and their per-batch results: idempotent creation, progress, terminal
/// outcomes and history.
/// </summary>
public sealed class SyncJobRepository(IDbConnection connection)
{
    private const string SelectColumns = """
        SELECT
            id                   AS Id,
            connection_id        AS ConnectionId,
            user_id              AS UserId,
            idempotency_key      AS IdempotencyKey,
            mode                 AS Mode,
            trigger              AS Trigger,
            status               AS Status,
            phase                AS Phase,
            books_total          AS BooksTotal,
            books_done           AS BooksDone,
            highlights_seen      AS HighlightsSeen,
            highlights_new       AS HighlightsNew,
            highlights_duplicate AS HighlightsDuplicate,
            truncated_books      AS TruncatedBooks,
            failure_code         AS FailureCode,
            failure_detail       AS FailureDetail,
            recovery_steps       AS RecoverySteps,
            started_at           AS StartedAt,
            ended_at             AS EndedAt
        FROM sync_jobs
        """;

    /// <summary>Returns the job stored for an idempotency key, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetByKeyAsync(int userId, string idempotencyKey) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE user_id = @UserId AND idempotency_key = @IdempotencyKey",
            new { UserId = userId, IdempotencyKey = idempotencyKey });

    /// <summary>Returns a job by id, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetByIdAsync(long jobId) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE id = @JobId",
            new { JobId = jobId });

    /// <summary>Returns the single running job for a connection, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetRunningAsync(long connectionId) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE connection_id = @ConnectionId AND status = 'running' ORDER BY id DESC LIMIT 1",
            new { ConnectionId = connectionId });

    /// <summary>Returns the most recent job for a connection, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetLatestAsync(long connectionId) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE connection_id = @ConnectionId ORDER BY id DESC LIMIT 1",
            new { ConnectionId = connectionId });

    /// <summary>Returns the most recent job that reached a terminal status, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetLatestTerminalAsync(long connectionId) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"""
            {SelectColumns}
            WHERE connection_id = @ConnectionId AND status <> 'running'
            ORDER BY id DESC LIMIT 1
            """,
            new { ConnectionId = connectionId });

    /// <summary>Inserts a new running job and returns it.</summary>
    public async Task<SyncJobRecord> InsertAsync(
        long connectionId,
        int userId,
        string idempotencyKey,
        string mode,
        string trigger,
        string startedAt)
    {
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO sync_jobs
                (connection_id, user_id, idempotency_key, mode, trigger, status, started_at)
            VALUES
                (@ConnectionId, @UserId, @IdempotencyKey, @Mode, @Trigger, 'running', @StartedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                ConnectionId = connectionId,
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                Mode = mode,
                Trigger = trigger,
                StartedAt = startedAt
            });

        return await GetByIdAsync(id)
            ?? throw new InvalidOperationException("The newly created sync job could not be read back.");
    }

    /// <summary>Updates the job phase and, when supplied, the book progress counters.</summary>
    public Task UpdatePhaseAsync(long jobId, string phase, int? booksTotal, int? booksDone) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET phase = @Phase,
                books_total = COALESCE(@BooksTotal, books_total),
                books_done = COALESCE(@BooksDone, books_done)
            WHERE id = @JobId
            """,
            new { JobId = jobId, Phase = phase, BooksTotal = booksTotal, BooksDone = booksDone });

    /// <summary>Adds the counters observed by a committed batch to the job totals.</summary>
    public Task AddCountersAsync(long jobId, int highlightsSeen, int newHighlights, int duplicates) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET highlights_seen = highlights_seen + @Seen,
                highlights_new = highlights_new + @NewHighlights,
                highlights_duplicate = highlights_duplicate + @Duplicates
            WHERE id = @JobId
            """,
            new { JobId = jobId, Seen = highlightsSeen, NewHighlights = newHighlights, Duplicates = duplicates });

    /// <summary>Stores a batch result; a replay of the same index is ignored.</summary>
    public Task SaveBatchAsync(SyncJobBatchRecord batch) =>
        connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO sync_job_batches
                (job_id, batch_index, new_highlights, duplicate_highlights, new_books, new_authors, received_at)
            VALUES
                (@JobId, @BatchIndex, @NewHighlights, @DuplicateHighlights, @NewBooks, @NewAuthors, @ReceivedAt)
            """,
            batch);

    /// <summary>Returns the stored result for a batch index, or <c>null</c> when not yet committed.</summary>
    public Task<SyncJobBatchRecord?> GetBatchAsync(long jobId, int batchIndex) =>
        connection.QuerySingleOrDefaultAsync<SyncJobBatchRecord>(
            """
            SELECT
                job_id               AS JobId,
                batch_index          AS BatchIndex,
                new_highlights       AS NewHighlights,
                duplicate_highlights AS DuplicateHighlights,
                new_books            AS NewBooks,
                new_authors          AS NewAuthors,
                received_at          AS ReceivedAt
            FROM sync_job_batches
            WHERE job_id = @JobId AND batch_index = @BatchIndex
            """,
            new { JobId = jobId, BatchIndex = batchIndex });

    /// <summary>Marks a job complete with its final counts.</summary>
    public Task CompleteAsync(
        long jobId,
        string status,
        int booksTotal,
        int booksDone,
        int truncatedBooks,
        string endedAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET status = @Status,
                phase = 'done',
                books_total = @BooksTotal,
                books_done = @BooksDone,
                truncated_books = @TruncatedBooks,
                ended_at = @EndedAt
            WHERE id = @JobId
            """,
            new
            {
                JobId = jobId,
                Status = status,
                BooksTotal = booksTotal,
                BooksDone = booksDone,
                TruncatedBooks = truncatedBooks,
                EndedAt = endedAt
            });

    /// <summary>Marks a job failed with a sanitized failure report.</summary>
    public Task FailAsync(
        long jobId,
        string status,
        string failureCode,
        string? failureDetail,
        string? recoverySteps,
        string endedAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET status = @Status,
                failure_code = @FailureCode,
                failure_detail = @FailureDetail,
                recovery_steps = @RecoverySteps,
                ended_at = @EndedAt
            WHERE id = @JobId
            """,
            new
            {
                JobId = jobId,
                Status = status,
                FailureCode = failureCode,
                FailureDetail = failureDetail,
                RecoverySteps = recoverySteps,
                EndedAt = endedAt
            });

    /// <summary>Marks a stale job interrupted.</summary>
    public Task InterruptAsync(long jobId, string endedAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET status = 'interrupted',
                failure_code = 'interrupted',
                ended_at = @EndedAt
            WHERE id = @JobId AND status = 'running'
            """,
            new { JobId = jobId, EndedAt = endedAt });

    /// <summary>Lists the most recent jobs for a connection, newest first.</summary>
    public async Task<IReadOnlyList<SyncJobRecord>> ListRecentAsync(long connectionId, int limit)
    {
        var rows = await connection.QueryAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE connection_id = @ConnectionId ORDER BY id DESC LIMIT @Limit",
            new { ConnectionId = connectionId, Limit = limit });

        return rows.AsList();
    }

    /// <summary>Returns running jobs started before the cutoff so they can be marked interrupted.</summary>
    public async Task<IReadOnlyList<SyncJobRecord>> GetStaleRunningAsync(string cutoffIso)
    {
        var rows = await connection.QueryAsync<SyncJobRecord>(
            $"""
            {SelectColumns}
            WHERE status = 'running' AND started_at IS NOT NULL AND started_at < @Cutoff
            """,
            new { Cutoff = cutoffIso });

        return rows.AsList();
    }
}
