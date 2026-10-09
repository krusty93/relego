using Relego.Server.Models;

namespace Relego.Server.Sync;

/// <summary>
/// Derives the user-visible sync status from a connection and its latest job. The status is computed
/// rather than stored.
/// </summary>
public static class SyncStatusDerivation
{
    /// <summary>
    /// Fixed heartbeat staleness window (five missed sixty-second heartbeats). It is independent of
    /// the configured sync frequency.
    /// </summary>
    public static readonly TimeSpan HeartbeatStalenessWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Derives the status string. Precedence: <c>auth_expired</c> &gt; <c>failed</c> &gt;
    /// <c>browser_not_reporting</c> &gt; <c>reminder_due</c> &gt; <c>syncing</c> &gt;
    /// <c>completed</c> &gt; <c>connected</c>.
    /// </summary>
    /// <param name="connection">The live connection, or <c>null</c> when the user has none.</param>
    /// <param name="currentJob">The running job, or <c>null</c>.</param>
    /// <param name="latestJob">The most recent terminal job, or <c>null</c>.</param>
    /// <param name="reminderDue">Whether an inactivity reminder is active.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The derived status.</returns>
    public static string Derive(
        SyncConnection? connection,
        SyncJobRecord? currentJob,
        SyncJobRecord? latestJob,
        bool reminderDue,
        DateTimeOffset now)
    {
        if (connection is null || connection.State == "disconnected")
            return "not_connected";

        if (connection.State == "pending_pairing")
            return "awaiting_pairing";

        var latest = currentJob ?? latestJob;

        if (connection.State == "auth_expired" || latest?.Status == "auth_expired")
            return "auth_expired";

        if (latest?.Status is "failed" or "interrupted")
            return "failed";

        if (!IsBrowserReporting(connection, now))
            return "browser_not_reporting";

        if (reminderDue)
            return "reminder_due";

        if (currentJob?.Status == "running")
            return "syncing";

        if (latest?.Status is "completed_with_changes" or "completed_no_changes")
            return "completed";

        return "connected";
    }

    /// <summary>
    /// Whether the extension has reported within <see cref="HeartbeatStalenessWindow"/> of
    /// <paramref name="now"/>, using the last heartbeat or, before any heartbeat, the pairing time.
    /// </summary>
    /// <param name="connection">The connection to evaluate.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when the extension is considered reachable.</returns>
    public static bool IsBrowserReporting(SyncConnection connection, DateTimeOffset now)
    {
        var reference = ParseTimestamp(connection.LastHeartbeatAt) ?? ParseTimestamp(connection.ConnectedAt);

        if (reference is null)
            return false;

        return now - reference.Value <= HeartbeatStalenessWindow;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
