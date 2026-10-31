using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Relego.Server.Models;
using Relego.Server.Sync;

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

    /// <summary>Returns the most recent successfully completed job, or <c>null</c>.</summary>
    public Task<SyncJobRecord?> GetLatestCompletedAsync(long connectionId) =>
        connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"""
            {SelectColumns}
            WHERE connection_id = @ConnectionId AND status LIKE 'completed%'
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

    /// <summary>Atomically starts or reuses a job, serializing writers across database connections.</summary>
    public async Task<(SyncJobRecord Job, bool Reused)> StartOrReuseAsync(
        long connectionId, int userId, string idempotencyKey, string mode, string trigger, string startedAt)
    {
        // SQLite's immediate transaction reserves the write lock before either lookup.
        using var transaction = ((SqliteConnection)connection).BeginTransaction(deferred: false);
        var existing = await connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE user_id = @UserId AND idempotency_key = @IdempotencyKey",
            new { UserId = userId, IdempotencyKey = idempotencyKey }, transaction);

        if (existing is not null)
        {
            if (existing.ConnectionId != connectionId)
                throw new SyncOperationException("The idempotency key belongs to a different connection. Use a new key.", StatusCodes.Status409Conflict);

            transaction.Commit();
            return (existing, true);
        }

        var running = await connection.QuerySingleOrDefaultAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE connection_id = @ConnectionId AND status = 'running'",
            new { ConnectionId = connectionId }, transaction);
        if (running is not null)
        {
            if (mode == "full_resync")
                throw new SyncOperationException($"A sync job ({running.Id}) is already running.", StatusCodes.Status409Conflict);

            transaction.Commit();
            return (running, true);
        }

        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO sync_jobs (connection_id, user_id, idempotency_key, mode, trigger, status, started_at)
            VALUES (@ConnectionId, @UserId, @IdempotencyKey, @Mode, @Trigger, 'running', @StartedAt);
            SELECT last_insert_rowid();
            """,
            new { ConnectionId = connectionId, UserId = userId, IdempotencyKey = idempotencyKey, Mode = mode, Trigger = trigger, StartedAt = startedAt },
            transaction);
        await connection.ExecuteAsync(
            """
            UPDATE sync_connections
            SET last_sync_started_at = @StartedAt,
                pending_command = CASE
                    WHEN pending_command = 'sync' OR (pending_command = 'full_resync' AND @Mode = 'full_resync')
                    THEN NULL ELSE pending_command END,
                state = CASE WHEN state = 'auth_expired' THEN 'active' ELSE state END,
                last_auth_expired_at = NULL
            WHERE id = @ConnectionId;
            """, new { ConnectionId = connectionId, StartedAt = startedAt, Mode = mode }, transaction);
        var job = await connection.QuerySingleAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE id = @Id", new { Id = id }, transaction);
        transaction.Commit();
        return (job, false);
    }

    /// <summary>Updates the job phase and, when supplied, the book progress counters.</summary>
    public async Task<int> UpdatePhaseAsync(long jobId, string phase, int? booksTotal, int? booksDone, string activityAt)
    {
        using var transaction = connection.BeginTransaction();
        var updated = await connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET phase = @Phase,
                books_total = COALESCE(@BooksTotal, books_total),
                books_done = COALESCE(@BooksDone, books_done)
            WHERE id = @JobId AND status = 'running'
            """,
            new { JobId = jobId, Phase = phase, BooksTotal = booksTotal, BooksDone = booksDone }, transaction);
        await RecordActivityAsync(jobId, activityAt, transaction);
        transaction.Commit();
        return updated;
    }

    /// <summary>Adds the counters observed by a committed batch to the job totals.</summary>
    public async Task AddCountersAsync(long jobId, int highlightsSeen, int newHighlights, int duplicates, string activityAt)
    {
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET highlights_seen = highlights_seen + @Seen,
                highlights_new = highlights_new + @NewHighlights,
                highlights_duplicate = highlights_duplicate + @Duplicates
            WHERE id = @JobId
            """,
            new { JobId = jobId, Seen = highlightsSeen, NewHighlights = newHighlights, Duplicates = duplicates }, transaction);
        await RecordActivityAsync(jobId, activityAt, transaction);
        transaction.Commit();
    }

    private Task RecordActivityAsync(long jobId, string activityAt, IDbTransaction transaction) =>
        connection.ExecuteAsync(
            """
            INSERT INTO sync_job_activity (job_id, user_id, last_activity_at)
            SELECT id, user_id, @ActivityAt FROM sync_jobs WHERE id = @JobId AND status = 'running'
            ON CONFLICT(job_id) DO UPDATE SET last_activity_at = MAX(last_activity_at, excluded.last_activity_at)
            """, new { JobId = jobId, ActivityAt = activityAt }, transaction);

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
    public Task<int> CompleteAsync(
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
            WHERE id = @JobId AND status = 'running'
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
    public Task<int> FailAsync(
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
            WHERE id = @JobId AND status = 'running'
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
    public Task<int> InterruptAsync(long jobId, string endedAt, string cutoffIso) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_jobs
            SET status = 'interrupted',
                failure_code = 'interrupted',
                ended_at = @EndedAt
            WHERE id = @JobId AND status = 'running'
              AND COALESCE(
                  (SELECT last_activity_at FROM sync_job_activity WHERE job_id = @JobId),
                  started_at) <= @Cutoff
            """,
            new { JobId = jobId, EndedAt = endedAt, Cutoff = cutoffIso });

    /// <summary>Lists the most recent jobs for a connection, newest first.</summary>
    public async Task<IReadOnlyList<SyncJobRecord>> ListRecentAsync(long connectionId, int limit)
    {
        var rows = await connection.QueryAsync<SyncJobRecord>(
            $"{SelectColumns} WHERE connection_id = @ConnectionId ORDER BY id DESC LIMIT @Limit",
            new { ConnectionId = connectionId, Limit = limit });

        return rows.AsList();
    }

    /// <summary>Returns jobs without activity since the cutoff so they can be marked interrupted.</summary>
    public async Task<IReadOnlyList<SyncJobRecord>> GetStaleRunningAsync(string cutoffIso)
    {
        var rows = await connection.QueryAsync<SyncJobRecord>(
            $"""
            {SelectColumns}
            WHERE status = 'running' AND COALESCE(
                (SELECT last_activity_at FROM sync_job_activity WHERE job_id = sync_jobs.id),
                started_at) <= @Cutoff
            """,
            new { Cutoff = cutoffIso });

        return rows.AsList();
    }
}
