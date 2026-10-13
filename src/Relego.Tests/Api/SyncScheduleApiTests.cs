using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl.Matchers;
using Relego.Core.Contracts;
using Relego.Server.Infrastructure.Database;

// Quartz also declares a JsonSerializerOptions, so the System.Text.Json one needs the alias here.
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the frequency surface of User Story 5. The invariants under test are the ones the user
/// depends on: only the ten documented values are accepted, the stored value is returned immediately
/// and survives a restart, the extension is told the value on its next heartbeat and the page is not
/// told a next run until the extension reports having applied it, and the server never creates a
/// routine sync schedule of its own.
/// </summary>
public sealed class SyncScheduleApiTests
{
    private const string Provider = "kindle-cloud";

    /// <summary>The ten choices of FR-025, in the order the page offers them.</summary>
    private static readonly int[] AllowedIntervals = [15, 30, 45, 60, 120, 240, 360, 720, 1080, 1440];

    private const int DefaultInterval = 360;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetStatus_NewConnectionDefaultsToSixHoursAndReportsItNotApplied()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(DefaultInterval, body!.Schedule.IntervalMinutes);
        Assert.Equal(AllowedIntervals, body.Schedule.AllowedIntervalMinutes);
        Assert.False(body.Schedule.Applied);
        Assert.Null(body.Schedule.NextRunAt);
        Assert.Equal("none", body.Schedule.PendingCommand);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1440)]
    public async Task PutSchedule_EveryAllowedFrequencyIsStoredAndReturnedWithinFiveSeconds(int intervalMinutes)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var elapsed = Stopwatch.StartNew();
        var (status, body) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = intervalMinutes },
            token);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        elapsed.Stop();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(intervalMinutes, body!.IntervalMinutes);
        Assert.Equal(intervalMinutes, summary!.Schedule.IntervalMinutes);
        Assert.Equal(intervalMinutes, await ReadStoredIntervalAsync(factory));

        // SC-011 allows five seconds; an in-process save and read must be orders of magnitude below.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5), $"save and read took {elapsed.Elapsed}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-15)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(3600)]
    [InlineData(10080)]
    public async Task PutSchedule_ValueOutsideTheTenIsRejectedAndKeepsTheStoredFrequency(int rejected)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var (accepted, _) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 120 },
            token);
        Assert.Equal(HttpStatusCode.OK, accepted);

        var (status, problem) = await SendAsync<ProblemPayload>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = rejected },
            token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);

        // The message has to name every choice the user does have, not just say "invalid".
        var message = problem!.Errors["intervalMinutes"].Single();
        foreach (var allowed in AllowedIntervals)
            Assert.Contains(allowed.ToString(), message, StringComparison.Ordinal);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal(120, summary!.Schedule.IntervalMinutes);
        Assert.Equal(120, await ReadStoredIntervalAsync(factory));
    }

    [Fact]
    public async Task PutSchedule_NextRunIsOnlyShownOnceTheExtensionReportsTheNewFrequency()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        // The extension reports the default it has been running so far.
        await HeartbeatAsync(client, token, DefaultInterval, "2030-01-01T06:00:00.000Z");
        var (_, before) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.True(before!.Schedule.Applied);
        Assert.Equal("2030-01-01T06:00:00.000Z", before.Schedule.NextRunAt);

        var (status, body) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 15 },
            token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(15, body!.IntervalMinutes);
        Assert.False(body.Applied);

        // The stored due time still belongs to the six-hour alarm, so it must not be presented as
        // the fifteen-minute frequency's next run.
        Assert.Null(body.NextRunAt);

        var (_, pending) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal(15, pending!.Schedule.IntervalMinutes);
        Assert.False(pending.Schedule.Applied);
        Assert.Null(pending.Schedule.NextRunAt);

        // The next heartbeat carries the frequency the extension actually applied.
        var heartbeat = await HeartbeatAsync(client, token, 15, "2030-01-01T00:15:00.000Z");
        Assert.Equal(15, heartbeat.IntervalMinutes);

        var (_, applied) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal(15, applied!.Schedule.IntervalMinutes);
        Assert.True(applied.Schedule.Applied);
        Assert.Equal("2030-01-01T00:15:00.000Z", applied.Schedule.NextRunAt);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(45)]
    [InlineData(240)]
    [InlineData(1440)]
    public async Task Heartbeat_CarriesTheStoredFrequencyAndNeverStartsASync(int intervalMinutes)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var (_, change) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = intervalMinutes },
            token);
        Assert.Equal(intervalMinutes, change!.IntervalMinutes);

        var heartbeat = await HeartbeatAsync(client, token, intervalMinutes, null);

        Assert.Equal(intervalMinutes, heartbeat.IntervalMinutes);
        Assert.Equal(60, heartbeat.HeartbeatSeconds);

        // The heartbeat is liveness only: it never queues a routine sync and never reports one.
        Assert.Equal("none", heartbeat.Command);
        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("none", summary!.Schedule.PendingCommand);
        Assert.Null(summary.CurrentJob);
        Assert.Null(summary.LastCompletedJob);
    }

    [Fact]
    public async Task PutSchedule_DoesNotDisturbARunningSync()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "running-job", "routine", "scheduled");

        var (status, _) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 1440 },
            token);

        Assert.Equal(HttpStatusCode.OK, status);

        // The new period applies to the next run; the run in flight is untouched and no command is queued.
        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal(1440, summary!.Schedule.IntervalMinutes);
        Assert.Equal("none", summary.Schedule.PendingCommand);
        Assert.Equal(jobId, summary.CurrentJob!.Id);
        Assert.Equal("running", summary.CurrentJob.Status);
    }

    [Fact]
    public async Task PutSchedule_CreatesNoRoutineSyncScheduleOnTheServer()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var jobsBefore = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var triggersBefore = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());

        var (status, _) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 15 },
            token);
        Assert.Equal(HttpStatusCode.OK, status);

        var jobsAfter = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var triggersAfter = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());

        Assert.Equal(jobsBefore, jobsAfter);
        Assert.Equal(triggersBefore, triggersAfter);

        // Quartz remains the inactivity reminder only (FR-026); a routine sync must never appear here.
        Assert.Contains(jobsAfter, key => key is { Name: "RecapJob", Group: "Recap" });
        Assert.All(jobsAfter, key => Assert.Equal("Recap", key.Group));
        Assert.All(triggersAfter, key => Assert.Equal("Recap", key.Group));
        Assert.DoesNotContain(jobsAfter, key => MentionsSync(key.Name) || MentionsSync(key.Group));
        Assert.DoesNotContain(triggersAfter, key => MentionsSync(key.Name) || MentionsSync(key.Group));
    }

    [Fact]
    public async Task PutSchedule_SurvivesAServerRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"relego-schedule-{Guid.NewGuid():N}.db");

        try
        {
            string token;

            using (var factory = CreateFileBackedFactory(databasePath))
            using (var client = factory.CreateClient())
            {
                token = await PairAsync(client);

                var (status, _) = await SendAsync<SyncScheduleChangeResponse>(
                    client,
                    HttpMethod.Put,
                    $"/sync/{Provider}/schedule",
                    new SyncScheduleChangeRequest { IntervalMinutes = 45 },
                    token);
                Assert.Equal(HttpStatusCode.OK, status);
            }

            // A second host over the same file is what a restarted server looks like: fresh Quartz
            // store, fresh services, nothing cached in memory.
            using var restarted = CreateFileBackedFactory(databasePath);
            using var restartedClient = restarted.CreateClient();

            var (statusAfter, summary) = await SendAsync<SyncStatusResponse>(
                restartedClient,
                HttpMethod.Get,
                $"/sync/{Provider}",
                token: token);

            Assert.Equal(HttpStatusCode.OK, statusAfter);
            Assert.Equal(45, summary!.Schedule.IntervalMinutes);
            Assert.Equal(45, await ReadStoredIntervalAsync(restarted));

            // The extension re-learns the frequency from the heartbeat it sends on start-up.
            var heartbeat = await HeartbeatAsync(restartedClient, token, 45, null);
            Assert.Equal(45, heartbeat.IntervalMinutes);
            Assert.Equal(60, heartbeat.HeartbeatSeconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task PutSchedule_WithoutToken_Returns401()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 60 });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task PutSchedule_MissingBody_Returns422()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var (status, _) = await SendAsync<SyncScheduleChangeResponse>(client, HttpMethod.Put, $"/sync/{Provider}/schedule", null, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(DefaultInterval, await ReadStoredIntervalAsync(factory));
    }

    [Fact]
    public async Task PutSchedule_UnknownProvider_Returns404()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<SyncScheduleChangeResponse>(
            client,
            HttpMethod.Put,
            "/sync/nope/schedule",
            new SyncScheduleChangeRequest { IntervalMinutes = 60 });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    private static bool MentionsSync(string value) =>
        value.Contains("sync", StringComparison.OrdinalIgnoreCase);

    private static RelegoTestApplicationFactory CreateFileBackedFactory(string databasePath) =>
        new(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IDbConnection>(_ => OpenDatabase(databasePath))));

    private static SqliteConnection OpenDatabase(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        new SchemaBootstrap().ApplyAsync(connection).GetAwaiter().GetResult();
        return connection;
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

    private static async Task<SyncHeartbeatResponse> HeartbeatAsync(
        HttpClient client,
        string token,
        int appliedIntervalMinutes,
        string? nextSyncDueAt)
    {
        var (status, body) = await SendAsync<SyncHeartbeatResponse>(client, HttpMethod.Post, $"/sync/{Provider}/heartbeat", new SyncHeartbeatRequest
        {
            ProfileVersion = "1",
            AppliedIntervalMinutes = appliedIntervalMinutes,
            NextSyncDueAt = nextSyncDueAt
        }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<string> ConnectAsync(HttpClient client)
    {
        var (status, body) = await SendAsync<CreateSyncConnectionResponse>(client, HttpMethod.Post, $"/sync/{Provider}/connection");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.NotNull(body);
        return body!.Token;
    }

    private static async Task<string> PairAsync(HttpClient client)
    {
        var token = await ConnectAsync(client);
        var (status, _) = await SendAsync<SyncPairResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/pair",
            new SyncPairRequest { ProfileVersion = "1", RegionId = "us" },
            token);

        Assert.Equal(HttpStatusCode.OK, status);
        return token;
    }

    private static async Task<long> StartJobAsync(
        HttpClient client,
        string token,
        string idempotencyKey,
        string mode,
        string trigger)
    {
        var (status, body) = await SendAsync<SyncJobStartResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/jobs",
            new SyncJobStartRequest { IdempotencyKey = idempotencyKey, Mode = mode, Trigger = trigger },
            token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        return body!.JobId;
    }

    private static async Task<int> ReadStoredIntervalAsync(RelegoTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        return await db.QuerySingleAsync<int>(
            "SELECT sync_interval_minutes FROM sync_connections WHERE provider_id = @Provider",
            new { Provider });
    }

    private sealed record ProblemPayload
    {
        public Dictionary<string, string[]> Errors { get; init; } = [];
    }
}
