namespace Relego.Server.Models;

/// <summary>
/// A stored per-batch import result so a replayed batch returns the original counts.
/// </summary>
public sealed class SyncJobBatchRecord
{
    /// <summary>Owning job id.</summary>
    public long JobId { get; set; }

    /// <summary>Zero-based batch index, unique per job.</summary>
    public int BatchIndex { get; set; }

    /// <summary>Highlights imported as new records.</summary>
    public int NewHighlights { get; set; }

    /// <summary>Highlights skipped as duplicates.</summary>
    public int DuplicateHighlights { get; set; }

    /// <summary>New book records created.</summary>
    public int NewBooks { get; set; }

    /// <summary>New author records created.</summary>
    public int NewAuthors { get; set; }

    /// <summary>Time the batch was first committed.</summary>
    public string ReceivedAt { get; set; } = string.Empty;
}
