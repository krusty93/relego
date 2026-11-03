using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the User Story 2 recovery surface: a forced resync queued as a single pending command,
/// the job history the user reads to understand a failure, and the two ways a job ends without
/// success. The invariants under test are the ones a user can observe: an expired session is
/// surfaced as its own status and repaired by a retry, a full resync of already imported content
/// reports no new highlights, a job whose browser went away becomes interrupted, and no job that
/// did not succeed ever advances the last completed sync.
/// </summary>
public sealed class SyncRecoveryApiTests
{
    private const string Provider = "kindle-cloud";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PostCommands_RetainsRetryUntilAJobStarts()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var (status, queued) = await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "sync" });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.NotNull(queued);
        Assert.Equal(Provider, queued!.ProviderId);
        Assert.Equal("sync", queued.PendingCommand);

        var (_, before) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("sync", before!.Schedule.PendingCommand);

        var first = await HeartbeatAsync(client, token);
        Assert.Equal("sync", first.Command);

        var second = await HeartbeatAsync(client, token);
        Assert.Equal("sync", second.Command);

        await StartJobAsync(client, token, "key-1", "routine", "manual");
        Assert.Equal("none", (await HeartbeatAsync(client, token)).Command);

        var (_, after) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("none", after!.Schedule.PendingCommand);
    }

    [Fact]
    public async Task PostCommands_FullResyncUpgradesAPendingRetryAndIsNeverDowngraded()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "sync" });

        var (upgraded, upgradedBody) = await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "full_resync" });

        Assert.Equal(HttpStatusCode.Accepted, upgraded);
        Assert.Equal("full_resync", upgradedBody!.PendingCommand);

        // A retry request must not silently cancel the full resync the user already asked for.
        var (_, kept) = await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "sync" });

        Assert.Equal("full_resync", kept!.PendingCommand);

        var heartbeat = await HeartbeatAsync(client, token);
        Assert.Equal("full_resync", heartbeat.Command);
    }

    [Theory]
    [InlineData("sync", "routine")]
    [InlineData("sync", "full_resync")]
    [InlineData("full_resync", "full_resync")]
    public async Task PostCommands_JobStartSatisfiesThePendingCommand(string command, string mode)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = command });
        await StartJobAsync(client, token, "key-1", mode, "manual");

        // The requested command is satisfied by the job it produced rather than left queued.
        var heartbeat = await HeartbeatAsync(client, token);
        Assert.Equal("none", heartbeat.Command);
    }

    [Fact]
    public async Task PostCommands_RoutineStartPreservesFullResyncUntilItCanStart()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "full_resync" });
        var routineId = await StartJobAsync(client, token, "routine-key", "routine", "scheduled");

        Assert.Equal("full_resync", (await HeartbeatAsync(client, token)).Command);
        var (rejected, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs",
            new SyncJobStartRequest { IdempotencyKey = "blocked-resync", Mode = "full_resync", Trigger = "manual" }, token);
        Assert.Equal(HttpStatusCode.Conflict, rejected);
        Assert.Equal("full_resync", (await HeartbeatAsync(client, token)).Command);

        await CompleteAsync(client, token, routineId, booksTotal: 0);
        await StartJobAsync(client, token, "resync-key", "full_resync", "manual");
        Assert.Equal("none", (await HeartbeatAsync(client, token)).Command);
    }

    [Fact]
    public async Task PostCommands_FullResyncRecoversAPairedAuthExpiredConnection()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "full_resync", "manual");
        await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/fail",
            new SyncJobFailRequest { Code = "auth_expired" }, token);

        await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "full_resync" });
        Assert.Equal("full_resync", (await HeartbeatAsync(client, token)).Command);
        var recoveryId = await StartJobAsync(client, token, "key-2", "full_resync", "retry");
        Assert.NotEqual(jobId, recoveryId);
        Assert.Equal("none", (await HeartbeatAsync(client, token)).Command);
        await CompleteAsync(client, token, recoveryId, booksTotal: 0);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("active", summary!.Connection.State);
        Assert.Equal("completed", summary.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostCommands_ReusedJobDoesNotAcknowledgeANewerCommand(bool completed)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "full_resync", "manual");
        if (completed)
            await CompleteAsync(client, token, jobId, booksTotal: 0);

        await SendAsync<SyncCommandResponse>(client, HttpMethod.Post, $"/sync/{Provider}/commands",
            new SyncCommandRequest { Command = "full_resync" });
        Assert.Equal(jobId, await StartJobAsync(client, token, "key-1", "full_resync", "manual"));
        Assert.Equal("full_resync", (await HeartbeatAsync(client, token)).Command);
    }

    [Theory]
    [InlineData("resync")]
    [InlineData("FULL_RESYNC")]
    [InlineData("")]
    public async Task PostCommands_UnknownCommand_Returns422ListingAcceptedValues(string command)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = command });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("command", problem!.Errors.Keys);
        Assert.Contains("full_resync", string.Join(" ", problem.Errors["command"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostCommands_UnpairedExtension_Returns409()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await ConnectAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "sync" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Pair", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostCommands_NotConnected_Returns409()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/commands", new SyncCommandRequest { Command = "sync" });

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task GetJobs_ReturnsHistoryNewestFirstWithItsOwnFailureReport()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var failedJobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");
        await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{failedJobId}/fail", new SyncJobFailRequest { Code = "network" }, token);

        var completedJobId = await StartJobAsync(client, token, "key-2", "routine", "scheduled");
        await CommitBatchAsync(client, token, completedJobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);
        await CompleteAsync(client, token, completedJobId, booksTotal: 1);

        var (status, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs?limit=20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(jobs);
        Assert.Equal(2, jobs!.Count);
        Assert.Equal(completedJobId, jobs[0].Id);
        Assert.Equal("completed_with_changes", jobs[0].Status);
        Assert.Null(jobs[0].FailureCode);
        Assert.Empty(jobs[0].RecoverySteps);

        // Each job carries its own report: the history is readable without the status endpoint.
        var failed = jobs[1];
        Assert.Equal(failedJobId, failed.Id);
        Assert.Equal("failed", failed.Status);
        Assert.Equal("network", failed.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(failed.FailureDetail));
        Assert.NotEmpty(failed.RecoverySteps);
    }

    [Fact]
    public async Task GetJobs_InterruptedJobReportsInterruptedRecovery()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        await AgeJobAsync(factory, jobId, TimeSpan.FromMinutes(16));
        await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        var (_, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs");

        Assert.NotNull(jobs);
        var job = Assert.Single(jobs!);
        Assert.Equal("interrupted", job.Status);
        Assert.Equal("interrupted", job.FailureCode);
        Assert.NotEmpty(job.RecoverySteps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task GetJobs_OutOfRangeLimit_Returns422(int limit)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Get, $"/sync/{Provider}/jobs?limit={limit}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("limit", problem!.Errors.Keys);
    }

    [Fact]
    public async Task GetJobs_NotConnected_ReturnsEmptyList()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(jobs!);
    }

    [Fact]
    public async Task GetJobs_UnknownProvider_Returns404ListingValidIds()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Get, "/sync/not-a-provider/jobs");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains(Provider, problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostFail_AuthExpiredIsRepairedByTheNextJob()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/fail", new SyncJobFailRequest { Code = "auth_expired" }, token);

        var (_, expired) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal("auth_expired", expired!.Status);
        Assert.Equal("auth_expired", expired.Connection.State);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
            Assert.NotNull(await db.QuerySingleAsync<string?>(
                "SELECT last_auth_expired_at FROM sync_connections WHERE provider_id = @Provider",
                new { Provider }));
        }

        await StartJobAsync(client, token, "key-2", "routine", "retry");

        var (_, repaired) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal("syncing", repaired!.Status);
        Assert.Equal("active", repaired.Connection.State);
        Assert.Null(repaired.Failure);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
            Assert.Null(await db.QuerySingleAsync<string?>(
                "SELECT last_auth_expired_at FROM sync_connections WHERE provider_id = @Provider",
                new { Provider }));
        }
    }

    [Fact]
    public async Task PostFail_FailedJobNeverAdvancesLastSyncCompleted()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/fail", new SyncJobFailRequest { Code = "server_rejected" }, token);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal("failed", summary!.Status);
        Assert.Equal("server_rejected", summary.Failure?.Code);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Null(await db.QuerySingleAsync<string?>(
            "SELECT last_sync_completed_at FROM sync_connections WHERE provider_id = @Provider",
            new { Provider }));
    }

    [Fact]
    public async Task StaleJob_IsMarkedInterruptedWithoutAdvancingLastSyncCompleted()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        // Simulates a browser that closed mid-sync: the job made no progress for over fifteen minutes.
        await AgeJobAsync(factory, jobId, TimeSpan.FromMinutes(16));

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("failed", summary!.Status);
        Assert.Equal("interrupted", summary.Failure?.Code);
        Assert.NotEmpty(summary.Failure!.RecoverySteps);

        // The swept job is terminal, so it is no longer the running job and it never completed.
        Assert.Null(summary.CurrentJob);
        Assert.Null(summary.LastCompletedJob);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Null(await db.QuerySingleAsync<string?>(
            "SELECT last_sync_completed_at FROM sync_connections WHERE provider_id = @Provider",
            new { Provider }));
    }

    [Fact]
    public async Task StaleJob_IsNotAdoptedAsTheRunningJobOfTheNextStart()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var abandonedId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        await AgeJobAsync(factory, abandonedId, TimeSpan.FromMinutes(16));

        var nextId = await StartJobAsync(client, token, "key-2", "routine", "scheduled");

        Assert.NotEqual(abandonedId, nextId);

        var (_, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs");

        Assert.Equal("interrupted", jobs![1].Status);
        Assert.Equal("running", jobs[0].Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleJob_RecentActivityKeepsAnOldJobRunning(bool reportProgress)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");
        await AgeJobAsync(factory, jobId, TimeSpan.FromMinutes(16));

        if (reportProgress)
        {
            var (status, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/progress",
                new SyncJobProgressRequest { Phase = "reading", BooksTotal = 2, BooksDone = 1 }, token);
            Assert.Equal(HttpStatusCode.NoContent, status);
        }
        else
        {
            await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 1)]);
        }

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal(jobId, summary!.CurrentJob?.Id);
        Assert.Equal(jobId, await StartJobAsync(client, token, "key-2", "routine", "scheduled"));
        await CompleteAsync(client, token, jobId, booksTotal: reportProgress ? 2 : 1);
    }

    [Fact]
    public async Task FullResync_OverAlreadyImportedContent_ReportsNoNewHighlights()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var firstJobId = await StartJobAsync(client, token, "key-1", "routine", "initial");
        await CommitBatchAsync(client, token, firstJobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);
        await CompleteAsync(client, token, firstJobId, booksTotal: 1);

        var resyncId = await StartJobAsync(client, token, "key-2", "full_resync", "manual");
        var replayed = await CommitBatchAsync(client, token, resyncId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);

        // Invariant 5: a full resync re-reads every book but never duplicates a highlight.
        Assert.Equal(0, replayed.NewHighlights);
        Assert.Equal(2, replayed.DuplicateHighlights);

        var (status, completed) = await SendAsync<SyncJobDto>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{resyncId}/complete", new SyncJobCompleteRequest { BooksTotal = 1, BooksDone = 1, TruncatedBooks = 0 }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("completed_no_changes", completed!.Status);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal("completed", summary!.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlights"));
    }

    private static async Task AgeJobAsync(RelegoTestApplicationFactory factory, long jobId, TimeSpan age)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        await db.ExecuteAsync(
            "UPDATE sync_jobs SET started_at = @StartedAt WHERE id = @JobId",
            new { JobId = jobId, StartedAt = DateTimeOffset.UtcNow.Subtract(age).ToString("o") });
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

    private static async Task<SyncHeartbeatResponse> HeartbeatAsync(HttpClient client, string token)
    {
        var (status, body) = await SendAsync<SyncHeartbeatResponse>(client, HttpMethod.Post, $"/sync/{Provider}/heartbeat", new SyncHeartbeatRequest
        {
            ProfileVersion = "1",
            AppliedIntervalMinutes = 360
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
        var (status, _) = await SendAsync<SyncPairResponse>(client, HttpMethod.Post, $"/sync/{Provider}/pair", new SyncPairRequest { ProfileVersion = "1", RegionId = "us" }, token);

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
        var (status, body) = await SendAsync<SyncJobStartResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = idempotencyKey, Mode = mode, Trigger = trigger }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        return body!.JobId;
    }

    private static async Task<SyncJobDto> CompleteAsync(HttpClient client, string token, long jobId, int booksTotal)
    {
        var (status, job) = await SendAsync<SyncJobDto>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/complete", new SyncJobCompleteRequest { BooksTotal = booksTotal, BooksDone = booksTotal, TruncatedBooks = 0 }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(job);
        return job!;
    }

    private static async Task<SyncBatchResponse> CommitBatchAsync(
        HttpClient client,
        string token,
        long jobId,
        int batchIndex,
        IReadOnlyList<(string ExternalKey, string Title, int HighlightCount)> books)
    {
        var payload = new
        {
            batchIndex,
            books = books.Select(book => new
            {
                externalKey = book.ExternalKey,
                title = book.Title,
                author = "Ada Lovelace",
                truncated = false,
                highlights = Enumerable.Range(0, book.HighlightCount)
                    .Select(index => new
                    {
                        externalId = $"{book.ExternalKey}-{index}",
                        text = $"Highlight {index} of {book.Title}",
                        note = (string?)null,
                        location = $"{index * 10}-{(index * 10) + 2}",
                        color = "yellow",
                        addedOn = (string?)null
                    })
                    .ToArray()
            }).ToArray()
        };

        var (status, body) = await SendAsync<SyncBatchResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/batches", payload, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        return body!;
    }

    private sealed record ProblemPayload
    {
        public string? Detail { get; init; }

        public string? Recovery { get; init; }

        public Dictionary<string, string[]> Errors { get; init; } = [];
    }
}
