using System.Data;
using Dapper;
using Relego.Server.Models;

namespace Relego.Server.Data;

/// <summary>
/// Persists cloud sync connections: creation, state, region, schedule, pending commands and
/// lifecycle timestamps. One live connection exists per <c>(user_id, provider_id)</c>.
/// </summary>
public sealed class SyncConnectionRepository(IDbConnection connection)
{
    private const string SelectColumns = """
        SELECT
            id                       AS Id,
            user_id                  AS UserId,
            provider_id              AS ProviderId,
            token_hash               AS TokenHash,
            state                    AS State,
            disclosure_version       AS DisclosureVersion,
            profile_version          AS ProfileVersion,
            region_id                AS RegionId,
            region_changed_at        AS RegionChangedAt,
            sync_interval_minutes    AS SyncIntervalMinutes,
            schedule_updated_at      AS ScheduleUpdatedAt,
            applied_interval_minutes AS AppliedIntervalMinutes,
            next_sync_due_at         AS NextSyncDueAt,
            pending_command          AS PendingCommand,
            connected_at             AS ConnectedAt,
            last_heartbeat_at        AS LastHeartbeatAt,
            last_sync_started_at     AS LastSyncStartedAt,
            last_sync_completed_at   AS LastSyncCompletedAt,
            last_new_content_at      AS LastNewContentAt,
            last_auth_expired_at     AS LastAuthExpiredAt,
            created_at               AS CreatedAt
        FROM sync_connections
        """;

    /// <summary>Returns the live (not disconnected) connection for a provider, or <c>null</c>.</summary>
    public Task<SyncConnection?> GetLiveAsync(int userId, string providerId) =>
        connection.QuerySingleOrDefaultAsync<SyncConnection>(
            $"""
            {SelectColumns}
            WHERE user_id = @UserId AND provider_id = @ProviderId AND state <> 'disconnected'
            """,
            new { UserId = userId, ProviderId = providerId });

    /// <summary>Returns a connection by id, including disconnected rows, or <c>null</c>.</summary>
    public Task<SyncConnection?> GetByIdAsync(long id) =>
        connection.QuerySingleOrDefaultAsync<SyncConnection>(
            $"{SelectColumns} WHERE id = @Id",
            new { Id = id });

    /// <summary>Creates a new connection in the <c>pending_pairing</c> state and returns it.</summary>
    public async Task<SyncConnection> CreateAsync(
        int userId,
        string providerId,
        string tokenHash,
        string disclosureVersion,
        string createdAt)
    {
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO sync_connections
                (user_id, provider_id, token_hash, state, disclosure_version, created_at)
            VALUES
                (@UserId, @ProviderId, @TokenHash, 'pending_pairing', @DisclosureVersion, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                UserId = userId,
                ProviderId = providerId,
                TokenHash = tokenHash,
                DisclosureVersion = disclosureVersion,
                CreatedAt = createdAt
            });

        return await GetByIdAsync(id)
            ?? throw new InvalidOperationException("The newly created sync connection could not be read back.");
    }

    /// <summary>Updates the connection state.</summary>
    public Task UpdateStateAsync(long id, string state) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET state = @State WHERE id = @Id",
            new { Id = id, State = state });

    /// <summary>Records the selected region and the time it changed.</summary>
    public Task SetRegionAsync(long id, string regionId, string changedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET region_id = @RegionId, region_changed_at = @ChangedAt WHERE id = @Id",
            new { Id = id, RegionId = regionId, ChangedAt = changedAt });

    /// <summary>Stores the sync frequency and the time it changed.</summary>
    public Task SetScheduleAsync(long id, int intervalMinutes, string updatedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET sync_interval_minutes = @IntervalMinutes, schedule_updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, IntervalMinutes = intervalMinutes, UpdatedAt = updatedAt });

    /// <summary>Stores the extension-reported applied frequency and next due time. Display-only.</summary>
    public Task SetAppliedScheduleAsync(long id, int? appliedIntervalMinutes, string? nextSyncDueAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET applied_interval_minutes = @AppliedIntervalMinutes, next_sync_due_at = @NextSyncDueAt WHERE id = @Id",
            new { Id = id, AppliedIntervalMinutes = appliedIntervalMinutes, NextSyncDueAt = nextSyncDueAt });

    /// <summary>
    /// Queues a single user-requested command. Idempotent, and a <c>sync</c> request never
    /// overwrites an already pending <c>full_resync</c>; it does upgrade a pending <c>sync</c>.
    /// </summary>
    public Task SetPendingCommandAsync(long id, string command) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_connections
            SET pending_command = @Command
            WHERE id = @Id
              AND (pending_command IS NULL OR (pending_command = 'sync' AND @Command = 'full_resync'))
            """,
            new { Id = id, Command = command });

    /// <summary>Clears and returns the pending command, or <c>null</c> when none was queued.</summary>
    public async Task<string?> ConsumePendingCommandAsync(long id)
    {
        var pending = await connection.QuerySingleOrDefaultAsync<string?>(
            "SELECT pending_command FROM sync_connections WHERE id = @Id",
            new { Id = id });

        if (pending is null)
            return null;

        await connection.ExecuteAsync(
            "UPDATE sync_connections SET pending_command = NULL WHERE id = @Id",
            new { Id = id });

        return pending;
    }

    /// <summary>Records the parse-profile version reported by the extension.</summary>
    public Task SetProfileVersionAsync(long id, string profileVersion) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET profile_version = @ProfileVersion WHERE id = @Id",
            new { Id = id, ProfileVersion = profileVersion });

    /// <summary>Records a heartbeat together with the extension-reported schedule. Display-only.</summary>
    public Task RecordHeartbeatAsync(
        long id,
        string? profileVersion,
        int? appliedIntervalMinutes,
        string? nextSyncDueAt,
        string heartbeatAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_connections
            SET last_heartbeat_at = @HeartbeatAt,
                profile_version = COALESCE(@ProfileVersion, profile_version),
                applied_interval_minutes = COALESCE(@AppliedIntervalMinutes, applied_interval_minutes),
                next_sync_due_at = COALESCE(@NextSyncDueAt, next_sync_due_at)
            WHERE id = @Id
            """,
            new
            {
                Id = id,
                HeartbeatAt = heartbeatAt,
                ProfileVersion = profileVersion,
                AppliedIntervalMinutes = appliedIntervalMinutes,
                NextSyncDueAt = nextSyncDueAt
            });

    /// <summary>Marks the connection as first paired.</summary>
    public Task MarkConnectedAsync(long id, string connectedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET connected_at = COALESCE(connected_at, @ConnectedAt), state = 'active', last_auth_expired_at = NULL WHERE id = @Id",
            new { Id = id, ConnectedAt = connectedAt });

    /// <summary>Records the start of a sync job.</summary>
    public Task RecordSyncStartedAsync(long id, string startedAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET last_sync_started_at = @StartedAt WHERE id = @Id",
            new { Id = id, StartedAt = startedAt });

    /// <summary>Records the completion of a sync job and, when applicable, new-content time.</summary>
    public Task RecordSyncCompletedAsync(long id, string completedAt, string? newContentAt) =>
        connection.ExecuteAsync(
            """
            UPDATE sync_connections
            SET last_sync_completed_at = @CompletedAt,
                last_new_content_at = COALESCE(@NewContentAt, last_new_content_at)
            WHERE id = @Id
            """,
            new { Id = id, CompletedAt = completedAt, NewContentAt = newContentAt });

    /// <summary>Marks the connection as having expired authentication.</summary>
    public Task RecordAuthExpiredAsync(long id, string occurredAt) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET state = 'auth_expired', last_auth_expired_at = @OccurredAt WHERE id = @Id",
            new { Id = id, OccurredAt = occurredAt });

    /// <summary>Clears an expired-authentication state after a successful pairing or sync.</summary>
    public Task ClearAuthExpiredAsync(long id) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET state = 'active', last_auth_expired_at = NULL WHERE id = @Id AND state = 'auth_expired'",
            new { Id = id });

    /// <summary>Disconnects the connection and revokes its token; highlights are left untouched.</summary>
    public Task DisconnectAsync(long id) =>
        connection.ExecuteAsync(
            "UPDATE sync_connections SET state = 'disconnected', token_hash = '', pending_command = NULL WHERE id = @Id",
            new { Id = id });
}
