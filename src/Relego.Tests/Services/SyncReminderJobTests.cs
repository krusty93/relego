using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Infrastructure.Smtp;
using Relego.Server.Models;
using Relego.Server.Services;
using Relego.Server.Sync;

namespace Relego.Tests.Services;

/// <summary>
/// Covers the inactivity sweep that <c>SyncReminderJob</c> runs daily. The invariants under test are
/// the ones a user can observe: a connection that has gone quiet for the provider's threshold is
/// reminded once for that quiet period and never twice, new content closes the reminder and starts a
/// new quiet period, and a reminder with no mail server behind it still reaches the user in the app.
/// The clock is injected, so the sweep is exercised at real thresholds rather than against a
/// shortened one.
/// </summary>
public sealed class SyncReminderJobTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 3, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SyncConnectionRepository _connections;
    private readonly SyncReminderRepository _reminders;
    private readonly UserRepository _users;
    private readonly FakeReminderMailDeliveryService _mail = new();
    private readonly MutableTimeProvider _clock = new(Now);
    private int _userId;

    public SyncReminderJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        _connections = new SyncConnectionRepository(_connection);
        _reminders = new SyncReminderRepository(_connection);
        _users = new UserRepository(_connection);
    }

    public async Task InitializeAsync()
    {
        await new SchemaBootstrap().ApplyAsync(_connection);
        _userId = await _users.EnsureUserAsync();
        await _users.UpdateDeliveryEmailAsync(_userId, "reader@example.com");
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunAsync_QuietForTheThreshold_RaisesOneReminderForThatQuietPeriod()
    {
        var connectedAt = Now.AddDays(-46);
        var connection = await SeedActiveConnectionAsync(connectedAt);

        var created = await SweepAsync();

        Assert.Equal(1, created);

        var reminder = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(reminder);
        Assert.Equal(connection.Id, reminder!.ConnectionId);
        Assert.Equal(_userId, reminder.UserId);
        Assert.Equal(45, reminder.ThresholdDays);
        // The quiet period starts at pairing: there was no new content to start it later.
        Assert.Equal(connectedAt, DateTimeOffset.Parse(reminder.QuietSince));
    }

    [Fact]
    public async Task RunAsync_OneDayShortOfTheThreshold_RaisesNothing()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-44));

        Assert.Equal(0, await SweepAsync());
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    [Fact]
    public async Task RunAsync_LaterNewContent_StartsTheQuietPeriodFromTheContent()
    {
        // Paired long ago, but a sync brought content in recently: the connection is not quiet yet.
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-400));
        await _connections.RecordSyncCompletedAsync(connection.Id, Now.AddDays(-2).ToString("o"), Now.AddDays(-2).ToString("o"));

        Assert.Equal(0, await SweepAsync());
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    [Fact]
    public async Task RunAsync_RepeatedSweepsOfTheSameQuietPeriod_NeverRemindTwice()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        Assert.Equal(1, await SweepAsync());
        Assert.Equal(0, await SweepAsync());
        Assert.Equal(0, await SweepAsync());

        var stored = await _connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM sync_reminders WHERE connection_id = @ConnectionId",
            new { ConnectionId = connection.Id });

        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task RunAsync_AfterNewContentClosesTheReminder_ANewQuietPeriodCanBeReminded()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));
        Assert.Equal(1, await SweepAsync());

        var first = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(first);

        // New content resolves the open reminder, exactly as a successful sync does.
        Assert.Equal(1, await _reminders.ResolveOpenAsync(connection.Id, Now.ToString("o")));
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));

        await _connections.RecordSyncCompletedAsync(connection.Id, Now.ToString("o"), Now.ToString("o"));
        _clock.Now = Now.AddDays(46);

        Assert.Equal(1, await SweepAsync());

        var second = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Equal(_clock.Now.AddDays(-46), DateTimeOffset.Parse(second.QuietSince));
    }

    [Fact]
    public async Task RunAsync_NoMailServerConfigured_KeepsTheReminderInTheApp()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        Assert.Equal(1, await SweepAsync(configureMail: false));

        var reminder = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(reminder);
        Assert.Null(reminder!.EmailedAt);
        Assert.Equal(0, _mail.NotificationSendCount);
    }

    [Fact]
    public async Task RunAsync_MailServerConfigured_EmailsOnceAndRecordsIt()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        Assert.Equal(1, await SweepAsync());

        var reminder = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(reminder);
        Assert.Equal(1, _mail.NotificationSendCount);
        Assert.Equal("reader@example.com", _mail.LastToAddress);
        Assert.Contains("45 days", _mail.LastPlainTextBody!, StringComparison.Ordinal);
        Assert.Equal(Now, DateTimeOffset.Parse(reminder!.EmailedAt!));
    }

    [Fact]
    public async Task RunAsync_UserHasNoDeliveryAddress_KeepsTheReminderInTheApp()
    {
        await _users.UpdateDeliveryEmailAsync(_userId, null);
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        Assert.Equal(1, await SweepAsync());

        Assert.NotNull(await _reminders.GetActiveAsync(connection.Id));
        Assert.Equal(0, _mail.NotificationSendCount);
    }

    [Fact]
    public async Task RunAsync_MailDeliveryFails_LeavesTheReminderInTheApp()
    {
        _mail.FailNotification = true;
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        // A bad mailbox must never turn into a failed sweep.
        Assert.Equal(1, await SweepAsync());

        var reminder = await _reminders.GetActiveAsync(connection.Id);
        Assert.NotNull(reminder);
        Assert.Null(reminder!.EmailedAt);
    }

    [Fact]
    public async Task RunAsync_UnknownProvider_SkipsTheConnectionWithoutRaisingAnything()
    {
        await _connections.CreateAsync(_userId, "not-a-provider", "hash", "1", Now.AddDays(-46).ToString("o"));
        await _connections.MarkConnectedAsync(
            await ConnectionIdAsync("not-a-provider"),
            Now.AddDays(-46).ToString("o"));

        Assert.Equal(0, await SweepAsync());

        var stored = await _connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM sync_reminders");
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task RunAsync_UnpairedConnection_IsNotSwept()
    {
        // A connection that never paired is not quiet: it was never live.
        var connection = await _connections.CreateAsync(_userId, KindleCloudProvider.ProviderId, "hash", "1", Now.AddDays(-400).ToString("o"));

        Assert.Equal(0, await SweepAsync());
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    [Fact]
    public async Task RunAsync_DisconnectedConnection_IsNotSwept()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));
        await _connections.DisconnectAsync(connection.Id);

        Assert.Equal(0, await SweepAsync());
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    private async Task<SyncConnection> SeedActiveConnectionAsync(DateTimeOffset connectedAt)
    {
        var connection = await _connections.CreateAsync(
            _userId,
            KindleCloudProvider.ProviderId,
            "hash",
            KindleCloudProvider.DisclosureVersion,
            connectedAt.ToString("o"));

        await _connections.MarkConnectedAsync(connection.Id, connectedAt.ToString("o"));

        return connection;
    }

    private async Task<long> ConnectionIdAsync(string providerId) =>
        await _connection.QuerySingleAsync<long>(
            "SELECT id FROM sync_connections WHERE provider_id = @ProviderId",
            new { ProviderId = providerId });

    private Task<int> SweepAsync(bool configureMail = true)
    {
        var smtpSettings = configureMail
            ? new SmtpSettings { FromAddress = "noreply@relego.local" }
            : new SmtpSettings();

        var service = new SyncReminderService(
            _connections,
            _reminders,
            _users,
            new SmtpConfigurationService(new SmtpSettingsRepository(_connection), Options.Create(smtpSettings)),
            [new KindleCloudProvider()],
            _mail,
            _clock,
            NullLogger<SyncReminderService>.Instance);

        return service.RunAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeReminderMailDeliveryService : IMailDeliveryService
    {
        public int NotificationSendCount { get; private set; }

        public string? LastToAddress { get; private set; }

        public string? LastPlainTextBody { get; private set; }

        public bool FailNotification { get; set; }

        public Task SendRecapAsync(string toAddress, byte[] epubContent, string fileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendTestEmailAsync(string toAddress, CancellationToken cancellationToken = default, SmtpSettings? smtpSettings = null) =>
            Task.CompletedTask;

        public Task SendHtmlRecapAsync(string toAddress, string htmlBody, string plainTextBody, string subject = "Your Relego Recap", CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendNotificationAsync(
            string toAddress,
            string subject,
            string htmlBody,
            string plainTextBody,
            CancellationToken cancellationToken = default)
        {
            if (FailNotification)
                throw new IOException("Simulated SMTP failure");

            NotificationSendCount++;
            LastToAddress = toAddress;
            LastPlainTextBody = plainTextBody;

            return Task.CompletedTask;
        }
    }
}
