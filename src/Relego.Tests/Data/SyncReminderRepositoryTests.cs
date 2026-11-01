using Microsoft.Data.Sqlite;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Models;
using Relego.Server.Sync;

namespace Relego.Tests.Data;

/// <summary>
/// Covers the guard that makes raising a reminder atomic with the quiet period it describes. The
/// inactivity sweep reads a connection and only later inserts, so a sync can end that quiet period in
/// between; the insert must then write nothing rather than raise — and email — a reminder for a period
/// that is already over.
/// </summary>
public sealed class SyncReminderRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 3, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SyncConnectionRepository _connections;
    private readonly SyncReminderRepository _reminders;
    private readonly UserRepository _users;
    private int _userId;

    public SyncReminderRepositoryTests()
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
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_ConnectionUnchangedSinceTheRead_RaisesTheReminder()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        var reminder = await RaiseAsync(connection);

        Assert.NotNull(reminder);
        Assert.Equal(connection.Id, reminder!.ConnectionId);
        Assert.Equal(_userId, reminder.UserId);
    }

    [Fact]
    public async Task CreateAsync_NewContentLandedAfterTheRead_WritesNothing()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));

        // The sync the sweep did not see: content arrived and the open reminder was resolved.
        await _connections.RecordSyncCompletedAsync(connection.Id, Now.ToString("o"), Now.ToString("o"));

        Assert.Null(await RaiseAsync(connection));
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    [Fact]
    public async Task CreateAsync_ConnectionDisconnectedAfterTheRead_WritesNothing()
    {
        var connection = await SeedActiveConnectionAsync(Now.AddDays(-46));
        await _connections.DisconnectAsync(connection.Id);

        Assert.Null(await RaiseAsync(connection));
        Assert.Null(await _reminders.GetActiveAsync(connection.Id));
    }

    /// <summary>Raises the reminder from the connection as the sweep read it, before any change.</summary>
    private Task<SyncReminder?> RaiseAsync(SyncConnection readConnection) =>
        _reminders.CreateAsync(
            readConnection.Id,
            readConnection.UserId,
            readConnection.ConnectedAt!,
            45,
            Now.ToString("o"),
            readConnection.LastNewContentAt,
            readConnection.ConnectedAt);

    private async Task<SyncConnection> SeedActiveConnectionAsync(DateTimeOffset connectedAt)
    {
        var connection = await _connections.CreateAsync(
            _userId,
            KindleCloudProvider.ProviderId,
            "hash",
            KindleCloudProvider.DisclosureVersion,
            connectedAt.ToString("o"));

        await _connections.MarkConnectedAsync(connection.Id, connectedAt.ToString("o"));

        return await _connections.GetByIdAsync(connection.Id)
            ?? throw new InvalidOperationException("The seeded connection could not be read back.");
    }
}
