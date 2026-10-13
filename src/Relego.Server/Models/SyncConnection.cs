namespace Relego.Server.Models;

/// <summary>
/// A persisted cloud sync connection between a user and a provider.
/// </summary>
public sealed class SyncConnection
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Owning user id.</summary>
    public int UserId { get; set; }

    /// <summary>Provider id, for example <c>kindle-cloud</c>.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the pairing token; the token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Connection state: <c>pending_pairing</c>, <c>active</c>, <c>auth_expired</c> or <c>disconnected</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Disclosure version shown to the user at setup.</summary>
    public string DisclosureVersion { get; set; } = string.Empty;

    /// <summary>Parse-profile version last reported by the extension.</summary>
    public string? ProfileVersion { get; set; }

    /// <summary>Allowlisted region id, or <c>null</c> until the extension pairs.</summary>
    public string? RegionId { get; set; }

    /// <summary>Time of the last region change, or <c>null</c>.</summary>
    public string? RegionChangedAt { get; set; }

    /// <summary>Stored sync frequency in minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 360;

    /// <summary>Time the frequency was last changed, or <c>null</c>.</summary>
    public string? ScheduleUpdatedAt { get; set; }

    /// <summary>Frequency the extension last reported applying, or <c>null</c>. Display-only.</summary>
    public int? AppliedIntervalMinutes { get; set; }

    /// <summary>Next due time of the extension's sync alarm, or <c>null</c>. Display-only.</summary>
    public string? NextSyncDueAt { get; set; }

    /// <summary>Pending user-requested command: <c>sync</c>, <c>full_resync</c> or <c>null</c>.</summary>
    public string? PendingCommand { get; set; }

    /// <summary>Time of the first successful pairing, or <c>null</c>.</summary>
    public string? ConnectedAt { get; set; }

    /// <summary>Time of the last extension heartbeat, or <c>null</c>.</summary>
    public string? LastHeartbeatAt { get; set; }

    /// <summary>Time the last sync job started, or <c>null</c>.</summary>
    public string? LastSyncStartedAt { get; set; }

    /// <summary>Time the last sync job completed, or <c>null</c>.</summary>
    public string? LastSyncCompletedAt { get; set; }

    /// <summary>Time new content was last imported, or <c>null</c>.</summary>
    public string? LastNewContentAt { get; set; }

    /// <summary>Time authentication last expired, or <c>null</c>.</summary>
    public string? LastAuthExpiredAt { get; set; }

    /// <summary>Row creation time.</summary>
    public string CreatedAt { get; set; } = string.Empty;
}
