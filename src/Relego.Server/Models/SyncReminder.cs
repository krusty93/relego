namespace Relego.Server.Models;

/// <summary>
/// A persisted inactivity reminder. One row exists per quiet period of a connection, so a period
/// the user has already been reminded about never produces a second reminder.
/// </summary>
public sealed class SyncReminder
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Connection the quiet period belongs to.</summary>
    public long ConnectionId { get; set; }

    /// <summary>Owning user id.</summary>
    public int UserId { get; set; }

    /// <summary>Start of the quiet period: the later of the last new content and the pairing time.</summary>
    public string QuietSince { get; set; } = string.Empty;

    /// <summary>Quiet days the provider requires before reminding.</summary>
    public int ThresholdDays { get; set; }

    /// <summary>Time the reminder was created.</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Time the reminder email was sent, or <c>null</c> when none was sent.</summary>
    public string? EmailedAt { get; set; }

    /// <summary>Time the user dismissed the reminder, or <c>null</c>.</summary>
    public string? DismissedAt { get; set; }

    /// <summary>Time the quiet period ended because new content synced, or <c>null</c>.</summary>
    public string? ResolvedAt { get; set; }
}
