using Relego.Server.Data;
using Relego.Server.Infrastructure.Smtp;
using Relego.Server.Models;
using Relego.Server.Services;

namespace Relego.Server.Sync;

/// <summary>
/// Sweeps active cloud sync connections for quiet periods. A connection that has seen no new
/// content for the provider's threshold raises one reminder for that quiet period: in the app
/// always, and by email only when a mail server and a delivery address are configured.
/// </summary>
/// <remarks>
/// The reminder is a nudge, never a sync: nothing here talks to Amazon or to the extension, and a
/// mail failure leaves the in-app reminder in place.
/// </remarks>
public sealed class SyncReminderService(
    SyncConnectionRepository connections,
    SyncReminderRepository reminders,
    UserRepository users,
    SmtpConfigurationService smtpConfiguration,
    IEnumerable<ICloudSyncProvider> providers,
    IMailDeliveryService mail,
    TimeProvider timeProvider,
    ILogger<SyncReminderService> logger)
{
    private const string EmailSubject = "Your Kindle highlights have gone quiet";

    /// <summary>
    /// Raises a reminder for every active connection whose quiet period has reached its provider's
    /// threshold and that has not been reminded about that period yet.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of reminders created.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var createdAt = now.ToString("o");
        var created = 0;

        foreach (var connection in await connections.ListActiveAsync())
        {
            try
            {
                created += await RemindAsync(connection, now, createdAt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One unusable connection must not stop the sweep for every other user.
                logger.LogError(
                    exception,
                    "Inactivity sweep failed for connection {ConnectionId}; it was skipped.",
                    connection.Id);
            }
        }

        return created;
    }

    /// <summary>
    /// Raises the reminder for a single connection when its quiet period has reached the provider's
    /// threshold.
    /// </summary>
    /// <returns><c>1</c> when a reminder was created, otherwise <c>0</c>.</returns>
    private async Task<int> RemindAsync(
        SyncConnection connection,
        DateTimeOffset now,
        string createdAt,
        CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(connection.ProviderId);
        if (provider is null)
        {
            logger.LogWarning(
                "Connection {ConnectionId} names unknown provider {ProviderId}; it was skipped.",
                connection.Id,
                connection.ProviderId);
            return 0;
        }

        if (QuietSince(connection) is not { } quietSince)
            return 0;

        if (now - quietSince < TimeSpan.FromDays(provider.InactivityReminderDays))
            return 0;

        // The stored instant is re-serialized so the unique index sees the same text on every
        // sweep; the raw column text may spell the same instant differently.
        var quietSinceText = quietSince.ToString("o");

        if (await reminders.ExistsForQuietPeriodAsync(connection.Id, quietSinceText))
            return 0;

        var reminder = await reminders.CreateAsync(
            connection.Id,
            connection.UserId,
            quietSinceText,
            provider.InactivityReminderDays,
            createdAt);

        logger.LogInformation(
            "Inactivity reminder {ReminderId} raised for connection {ConnectionId} after {Days} quiet days.",
            reminder.Id,
            connection.Id,
            provider.InactivityReminderDays);

        await TryEmailAsync(connection, reminder, cancellationToken);

        return 1;
    }

    /// <summary>
    /// Start of the quiet period: the later of the last new content and the pairing time, falling
    /// back to row creation for a connection that somehow never recorded either.
    /// </summary>
    private static DateTimeOffset? QuietSince(SyncConnection connection)
    {
        var newContent = ParseTimestamp(connection.LastNewContentAt);
        var connected = ParseTimestamp(connection.ConnectedAt);

        if (newContent is not null && connected is not null)
            return newContent > connected ? newContent : connected;

        return newContent ?? connected ?? ParseTimestamp(connection.CreatedAt);
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(
                value,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;

    private ICloudSyncProvider? ResolveProvider(string providerId) =>
        providers.FirstOrDefault(provider =>
            string.Equals(provider.Descriptor.Id, providerId, StringComparison.Ordinal));

    /// <summary>
    /// Emails the reminder when a mail server and a delivery address both exist. Every failure is
    /// logged and swallowed: the reminder is already stored, and one bad mailbox must not stop the
    /// sweep for everyone else.
    /// </summary>
    private async Task TryEmailAsync(
        SyncConnection connection,
        SyncReminder reminder,
        CancellationToken cancellationToken)
    {
        var effective = await smtpConfiguration.GetEffectiveAsync();
        if (effective.Origin == SmtpSettingsOrigin.Default)
        {
            logger.LogInformation(
                "No mail server is configured; reminder {ReminderId} stays in the app.",
                reminder.Id);
            return;
        }

        var user = await users.GetByIdAsync(connection.UserId);
        if (string.IsNullOrWhiteSpace(user.DeliveryEmail))
        {
            logger.LogInformation(
                "User {UserId} has no delivery address; reminder {ReminderId} stays in the app.",
                connection.UserId,
                reminder.Id);
            return;
        }

        try
        {
            await mail.SendNotificationAsync(
                user.DeliveryEmail,
                EmailSubject,
                BuildHtmlBody(reminder),
                BuildTextBody(reminder),
                cancellationToken);

            await reminders.MarkEmailedAsync(reminder.Id, timeProvider.GetUtcNow().ToString("o"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Reminder {ReminderId} could not be emailed; it stays in the app.",
                reminder.Id);
        }
    }

    private static string BuildHtmlBody(SyncReminder reminder) =>
        $"""
        <p>Nothing new has reached Relego from your Kindle cloud for {reminder.ThresholdDays} days.</p>
        <p>Open the Relego page to see whether the browser extension is still connected. If it is,
        Amazon simply has no new highlights to send.</p>
        <p>You can always import <code>My Clippings.txt</code> or a Kobo database by hand; that path
        never needs the extension.</p>
        """;

    private static string BuildTextBody(SyncReminder reminder) =>
        $"""
        Nothing new has reached Relego from your Kindle cloud for {reminder.ThresholdDays} days.

        Open the Relego page to see whether the browser extension is still connected. If it is,
        Amazon simply has no new highlights to send.

        You can always import My Clippings.txt or a Kobo database by hand; that path never needs
        the extension.
        """;
}
