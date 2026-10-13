using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Models;
using Relego.Server.Sync;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the inactivity reminder as the web UI sees it: the status endpoint reports an open reminder
/// and the derived <c>reminder_due</c> status, and the dismiss endpoint closes it. The invariants
/// under test are the ones a user can observe: a reminder that is open is visible, dismissing it
/// makes it stop coming back, dismissing twice is not an error, and a reminder belonging to another
/// connection cannot be dismissed or even confirmed to exist.
/// </summary>
public sealed class SyncReminderApiTests
{
    private const string Provider = KindleCloudProvider.ProviderId;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetStatus_OpenReminder_ReportsItAndTheDerivedStatus()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);
        var reminder = await SeedReminderAsync(factory, quietSince: DateTimeOffset.UtcNow.AddDays(-46));

        var (status, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("reminder_due", body!.Status);
        Assert.NotNull(body.Reminder);
        Assert.Equal(reminder.Id, body.Reminder!.Id);
        Assert.Equal(45, body.Reminder.ThresholdDays);
        Assert.False(body.Reminder.Dismissed);
    }

    [Fact]
    public async Task GetStatus_NoReminder_ReportsNoneAndNoDerivedStatus()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(body);
        Assert.Null(body!.Reminder);
        Assert.NotEqual("reminder_due", body.Status);
    }

    [Fact]
    public async Task GetStatus_DismissedReminder_StopsBeingReported()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);
        var reminder = await SeedReminderAsync(factory, quietSince: DateTimeOffset.UtcNow.AddDays(-46));

        var dismiss = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{reminder.Id}/dismiss");
        Assert.Equal(HttpStatusCode.NoContent, dismiss.Status);

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(body);
        Assert.Null(body!.Reminder);
        Assert.NotEqual("reminder_due", body.Status);
        Assert.NotNull(await DismissedAtAsync(factory, reminder.Id));
    }

    [Fact]
    public async Task PostDismiss_IsUnauthenticatedBecauseTheBannerIsShownByTheWebUi()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);
        var reminder = await SeedReminderAsync(factory, quietSince: DateTimeOffset.UtcNow.AddDays(-46));

        // No pairing token on the request: the management surface is not the extension channel.
        var (status, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{reminder.Id}/dismiss");

        Assert.Equal(HttpStatusCode.NoContent, status);
    }

    [Fact]
    public async Task PostDismiss_Twice_SucceedsTheSecondTimeWithoutChangingTheFirstDismissal()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);
        var reminder = await SeedReminderAsync(factory, quietSince: DateTimeOffset.UtcNow.AddDays(-46));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{reminder.Id}/dismiss")).Status);

        var first = await DismissedAtAsync(factory, reminder.Id);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{reminder.Id}/dismiss")).Status);

        // The banner is already gone, so the second request is a no-op rather than a rewrite.
        Assert.Equal(first, await DismissedAtAsync(factory, reminder.Id));
    }

    [Fact]
    public async Task PostDismiss_UnknownReminder_ReturnsNotFound()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/424242/dismiss");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.NotNull(problem);
        Assert.Equal("Unknown reminder", problem!.Title);
    }

    [Fact]
    public async Task PostDismiss_ReminderOfADisconnectedConnection_ReturnsNotFound()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        // A reminder left over from a connection the user has since disconnected. The new pairing
        // below is a different connection, so the stale reminder is not the one on screen.
        var stale = await SeedStaleReminderAsync(factory);
        var token = await PairAsync(client);

        var (status, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{stale.Id}/dismiss", token: token);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Null(await DismissedAtAsync(factory, stale.Id));
    }

    [Fact]
    public async Task PostDismiss_NoConnectionAtAll_ReturnsNotFound()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var reminder = await SeedStaleReminderAsync(factory);

        var (status, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/reminders/{reminder.Id}/dismiss");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Null(await DismissedAtAsync(factory, reminder.Id));
    }

    [Fact]
    public async Task PostDismiss_UnknownProvider_ReturnsNotFound()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<object>(client, HttpMethod.Post, "/sync/not-a-provider/reminders/1/dismiss");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    private static async Task<SyncReminder> SeedReminderAsync(RelegoTestApplicationFactory factory, DateTimeOffset quietSince)
    {
        var connectionId = await LiveConnectionIdAsync(factory);

        return await SeedReminderForAsync(factory, connectionId, quietSince);
    }

    private static async Task<SyncReminder> SeedReminderForAsync(
        RelegoTestApplicationFactory factory,
        long connectionId,
        DateTimeOffset quietSince)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var userId = await db.QuerySingleAsync<int>("SELECT user_id FROM sync_connections WHERE id = @Id", new { Id = connectionId });

        return await new SyncReminderRepository(db).CreateAsync(
            connectionId,
            userId,
            quietSince.ToString("o"),
            new KindleCloudProvider().InactivityReminderDays,
            DateTimeOffset.UtcNow.ToString("o"));
    }

    private static async Task<long> LiveConnectionIdAsync(RelegoTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        return await db.QuerySingleAsync<long>(
            "SELECT id FROM sync_connections WHERE provider_id = @ProviderId AND state = 'active'",
            new { ProviderId = Provider });
    }

    /// <summary>
    /// Seeds a reminder belonging to a connection the user has already disconnected, creating the
    /// user when this is the first thing the test does. Only one live connection per provider is
    /// allowed, so a disconnected one has to exist before the test pairs.
    /// </summary>
    private static async Task<SyncReminder> SeedStaleReminderAsync(RelegoTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var connections = new SyncConnectionRepository(db);
        var quietSince = DateTimeOffset.UtcNow.AddDays(-46);
        var userId = await new UserRepository(db).EnsureUserAsync();

        var connection = await connections.CreateAsync(
            userId,
            Provider,
            "hash",
            KindleCloudProvider.DisclosureVersion,
            quietSince.ToString("o"));

        await connections.MarkConnectedAsync(connection.Id, quietSince.ToString("o"));
        await connections.DisconnectAsync(connection.Id);

        return await new SyncReminderRepository(db).CreateAsync(
            connection.Id,
            userId,
            quietSince.ToString("o"),
            new KindleCloudProvider().InactivityReminderDays,
            DateTimeOffset.UtcNow.ToString("o"));
    }

    private static async Task<string?> DismissedAtAsync(RelegoTestApplicationFactory factory, long reminderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        return await db.QuerySingleAsync<string?>(
            "SELECT dismissed_at FROM sync_reminders WHERE id = @Id",
            new { Id = reminderId });
    }

    private static async Task<string> PairAsync(HttpClient client)
    {
        var (created, connection) = await SendAsync<CreateSyncConnectionResponse>(client, HttpMethod.Post, $"/sync/{Provider}/connection");
        Assert.Equal(HttpStatusCode.Created, created);
        Assert.NotNull(connection);

        var (paired, _) = await SendAsync<SyncPairResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/pair",
            new SyncPairRequest { ProfileVersion = KindleCloudProvider.ProfileVersion, RegionId = "us" },
            connection!.Token);

        Assert.Equal(HttpStatusCode.OK, paired);

        return connection.Token;
    }

    private static async Task<(HttpStatusCode Status, T? Body)> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string url,
        object? payload = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: Json);

        if (token is not null)
            request.Headers.Add("Authorization", $"Bearer {token}");

        using var response = await client.SendAsync(request);

        if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength is null or 0)
            return (response.StatusCode, default);

        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        return (response.StatusCode, body);
    }

    private sealed record ProblemPayload
    {
        public string? Title { get; init; }

        public string? Detail { get; init; }
    }
}
