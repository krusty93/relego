namespace Relego.Server.Models;

/// <summary>
/// A persisted sync job run by a connection.
/// </summary>
public sealed class SyncJobRecord
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Owning connection id.</summary>
    public long ConnectionId { get; set; }

    /// <summary>Owning user id.</summary>
    public int UserId { get; set; }

    /// <summary>Client-generated idempotency key.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Job mode: <c>routine</c> or <c>full_resync</c>.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Why the job started.</summary>
    public string Trigger { get; set; } = string.Empty;

    /// <summary>Job status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Current phase, or <c>null</c>.</summary>
    public string? Phase { get; set; }

    /// <summary>Total books discovered, or <c>null</c>.</summary>
    public int? BooksTotal { get; set; }

    /// <summary>Books processed so far, or <c>null</c>.</summary>
    public int? BooksDone { get; set; }

    /// <summary>Highlights observed so far.</summary>
    public int HighlightsSeen { get; set; }

    /// <summary>New highlights committed so far.</summary>
    public int HighlightsNew { get; set; }

    /// <summary>Duplicate highlights skipped so far.</summary>
    public int HighlightsDuplicate { get; set; }

    /// <summary>Books where the export-limit truncation warning was observed.</summary>
    public int TruncatedBooks { get; set; }

    /// <summary>Stable failure code, or <c>null</c>.</summary>
    public string? FailureCode { get; set; }

    /// <summary>Sanitized failure detail, or <c>null</c>.</summary>
    public string? FailureDetail { get; set; }

    /// <summary>JSON array of recovery steps, or <c>null</c>.</summary>
    public string? RecoverySteps { get; set; }

    /// <summary>Job start time, or <c>null</c>.</summary>
    public string? StartedAt { get; set; }

    /// <summary>Job end time, or <c>null</c> while running.</summary>
    public string? EndedAt { get; set; }
}
