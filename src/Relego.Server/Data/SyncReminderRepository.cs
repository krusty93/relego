using System.Data;
using Dapper;
using Relego.Server.Models;

namespace Relego.Server.Data;

/// <summary>
/// Persists inactivity reminders. A reminder covers one quiet period of one connection; the
/// <c>uq_sync_reminders_connection_quiet</c> index is what guarantees a period is never reminded
/// about twice, even if the sweep runs concurrently.
/// </summary>
public sealed class SyncReminderRepository(IDbConnection connection)
{
    private const string SelectColumns = """
        SELECT
            id             AS Id,
            connection_id  AS ConnectionId,
            user_id        AS UserId,
            quiet_since    AS QuietSince,
            threshold_days AS ThresholdDays,
            created_at     AS CreatedAt,
            emailed_at     AS EmailedAt,
            dismissed_at   AS DismissedAt,
            resolved_at    AS ResolvedAt
        FROM sync_reminders
        """;

    /// <summary>
    /// Returns the active reminder for a connection — one that is neither dismissed nor resolved —
    /// or <c>null</c> when the connection is not waiting on a reminder.
    /// </summary>
    public Task<SyncReminder?> GetActiveAsync(long connectionId) =>
        connection.QuerySingleOrDefaultAsync<SyncReminder>(
            $"""
            {SelectColumns}
            WHERE connection_id = @ConnectionId AND dismissed_at IS NULL AND resolved_at IS NULL
            ORDER BY id DESC
            LIMIT 1
            """,
            new { ConnectionId = connectionId });

    /// <summary>Returns a reminder by id regardless of state, or <c>null</c>.</summary>
    public Task<SyncReminder?> GetByIdAsync(long id) =>
        connection.QuerySingleOrDefaultAsync<SyncReminder>(
            $"{SelectColumns} WHERE id = @Id",
            new { Id = id });

    /// <summary>Whether a reminder already exists for a connection's quiet period.</summary>
    public async Task<bool> ExistsForQuietPeriodAsync(long connectionId, string quietSince)
    {
        var count = await connection.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM sync_reminders
            WHERE connection_id = @ConnectionId AND quiet_since = @QuietSince
            """,
            new { ConnectionId = connectionId, QuietSince = quietSince });

        return count > 0;
    }

    /// <summary>Creates a reminder for a quiet period and returns it.</summary>
    /// <param name="connectionId">Connection the quiet period belongs to.</param>
    /// <param name="userId">Owning user id.</param>
    /// <param name="quietSince">Start of the quiet period.</param>
    /// <param name="thresholdDays">Quiet days the provider requires.</param>
    /// <param name="createdAt">Time the reminder was created.</param>
    /// <returns>The created reminder.</returns>
    public async Task<SyncReminder> CreateAsync(
        long connectionId,
        int userId,
        string quietSince,
        int thresholdDays,
        string createdAt)
    {
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO sync_reminders
                (connection_id, user_id, quiet_since, threshold_days, created_at)
            VALUES
                (@ConnectionId, @UserId, @QuietSince, @ThresholdDays, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                ConnectionId = connectionId,
                UserId = userId,
                QuietSince = quietSince,
                ThresholdDays = thresholdDays,
                CreatedAt = createdAt
            });

        return await GetByIdAsync(id)
            ?? throw new InvalidOperationException("The newly created sync reminder could not be read back.");
    }

    /// <summary>Records that the reminder email was sent.</summary>
    public Task MarkEmailedAsync(long id, string emailedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_reminders SET emailed_at = @EmailedAt WHERE id = @Id",
            new { Id = id, EmailedAt = emailedAt });

    /// <summary>Records that the user dismissed the reminder.</summary>
    public Task DismissAsync(long id, string dismissedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_reminders SET dismissed_at = @DismissedAt WHERE id = @Id",
            new { Id = id, DismissedAt = dismissedAt });

    /// <summary>
    /// Closes every open reminder of a connection. Called when new content syncs: the quiet period
    /// is over, so a later quiet period starts from the new content time.
    /// </summary>
    /// <returns>The number of reminders closed.</returns>
    public Task<int> ResolveOpenAsync(long connectionId, string resolvedAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_reminders
            SET resolved_at = @ResolvedAt
            WHERE connection_id = @ConnectionId AND resolved_at IS NULL
            """,
            new { ConnectionId = connectionId, ResolvedAt = resolvedAt });
}
