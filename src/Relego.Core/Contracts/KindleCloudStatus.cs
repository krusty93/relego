namespace Relego.Core.Contracts;

/// <summary>
/// Kindle cloud synchronization state summarised for the CLI. It is derived server-side from the
/// same rules the web UI uses, so both surfaces always agree.
/// </summary>
public sealed record KindleCloudStatus
{
    /// <summary>Provider id, for example <c>kindle-cloud</c>.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Derived status: <c>not_connected</c>, <c>awaiting_pairing</c>, <c>auth_expired</c>,
    /// <c>failed</c>, <c>browser_not_reporting</c>, <c>reminder_due</c>, <c>syncing</c>,
    /// <c>completed</c> or <c>connected</c>.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Allowlisted region id the extension last reported, or <c>null</c>.</summary>
    public string? RegionId { get; set; }

    /// <summary>Stored sync frequency in minutes, or <c>null</c> when there is no connection.</summary>
    public int? SyncIntervalMinutes { get; set; }

    /// <summary>Books processed by the running or last completed job, or <c>null</c>.</summary>
    public int? BooksDone { get; set; }

    /// <summary>Books discovered by the running or last completed job, or <c>null</c>.</summary>
    public int? BooksTotal { get; set; }

    /// <summary>New highlights committed by the last completed job, or <c>null</c>.</summary>
    public int? HighlightsNew { get; set; }

    /// <summary>Last successful sync completion time in ISO 8601 format, or <c>null</c>.</summary>
    public string? LastSyncCompletedAt { get; set; }

    /// <summary>
    /// Sanitized failure detail when the status is <c>failed</c> or <c>auth_expired</c>, or
    /// <c>null</c>. Page content, cookies and tokens are never part of it.
    /// </summary>
    public string? FailureDetail { get; set; }
}
