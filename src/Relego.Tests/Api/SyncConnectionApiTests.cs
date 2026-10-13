using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Server.Sync;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the User Story 1 REST surface: pairing, single-flight job start, idempotent batch commit
/// and explicit completion. The invariants under test are the ones a user can observe: the token is
/// shown exactly once, overlapping alarm firings never produce two concurrent jobs, a replayed batch
/// never changes totals, and a job never reports success without matching counts.
/// </summary>
public sealed class SyncConnectionApiTests
{
    private const string Provider = "kindle-cloud";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PostConnection_ReturnsTokenOnceAndStoresOnlyItsHash()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync<CreateSyncConnectionResponse>(client, HttpMethod.Post, $"/sync/{Provider}/connection");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.NotNull(body);
        Assert.Equal(Provider, body!.ProviderId);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var stored = await db.QuerySingleAsync<string>(
            "SELECT token_hash FROM sync_connections WHERE provider_id = @Provider",
            new { Provider });

        Assert.NotEqual(body.Token, stored);
        Assert.Equal(SyncTokenService.HashToken(body.Token), stored);

        // Invariant 3: nothing persisted anywhere carries the raw token.
        var leaked = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sync_connections WHERE token_hash LIKE @Fragment OR provider_id LIKE @Fragment",
            new { Fragment = $"%{body.Token}%" });
        Assert.Equal(0, leaked);
    }

    [Fact]
    public async Task PostConnection_UnknownProvider_Returns404ListingValidIds()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, "/sync/not-a-provider/connection");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.NotNull(problem);
        Assert.Contains(Provider, problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostPair_ActivatesConnectionAndReturnsDefaultInterval()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await ConnectAsync(client);

        var (status, paired) = await SendAsync<SyncPairResponse>(client, HttpMethod.Post, $"/sync/{Provider}/pair", new SyncPairRequest { ProfileVersion = "1", RegionId = "us" }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(paired);
        Assert.Equal("active", paired!.State);
        Assert.Equal(360, paired.IntervalMinutes);
        Assert.Equal(10, paired.AllowedIntervalMinutes.Count);
        Assert.Equal("us", paired.Region?.Id);
        Assert.Equal("read.amazon.com", paired.Region?.Host);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("connected", summary!.Status);
        Assert.Equal("active", summary.Connection.State);
        Assert.Equal("us", summary.Connection.Region?.Id);
        Assert.Equal(360, summary.Schedule.IntervalMinutes);
        Assert.Equal("none", summary.Schedule.PendingCommand);
        Assert.False(summary.Schedule.Applied);
    }

    [Fact]
    public async Task PostPair_WithoutToken_Returns401WithRepairRecovery()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await ConnectAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/pair", new SyncPairRequest { ProfileVersion = "1", RegionId = "us" });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.NotNull(problem);
        Assert.Equal("re-pair", problem!.Recovery);
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("cn")]
    [InlineData("dk")]
    [InlineData("ie")]
    [InlineData("pl")]
    [InlineData("read.amazon.com")]
    [InlineData("https://read.amazon.co.uk/notebook")]
    public async Task PostPair_UnsupportedOrHostLikeRegion_Returns422ListingSupportedIds(string regionId)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await ConnectAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/pair", new SyncPairRequest { ProfileVersion = "1", RegionId = regionId }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.NotNull(problem);
        Assert.Contains("regionId", problem!.Errors.Keys);
        Assert.Contains("us", problem.Errors["regionId"][0], StringComparison.Ordinal);

        // Hosts are never accepted from clients, so the connection stays unpaired.
        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Null(summary!.Connection.Region);
        Assert.Equal("awaiting_pairing", summary.Status);
    }

    [Fact]
    public async Task PostJobs_OverlappingStartsYieldExactlyOneRunningJob()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var first = await StartJobAsync(client, token, "key-1", "routine", "scheduled");
        var second = await StartJobAsync(client, token, "key-2", "routine", "scheduled");

        Assert.Equal(first, second);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var running = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs WHERE status = 'running'");
        Assert.Equal(1, running);

        // Invariant 7: a silent browser never advances last_sync_completed_at.
        var completedAt = await db.QuerySingleAsync<string?>(
            "SELECT last_sync_completed_at FROM sync_connections WHERE provider_id = @Provider",
            new { Provider });
        Assert.Null(completedAt);
    }

    [Fact]
    public async Task PostJobs_ReplayedIdempotencyKeyReturnsTheSameJob()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var first = await StartJobAsync(client, token, "key-1", "routine", "initial");
        var replay = await StartJobAsync(client, token, "key-1", "routine", "initial");

        Assert.Equal(first, replay);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var total = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs");
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task PostJobs_FullResyncWhileRunning_Returns409NamingTheRunningJob()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var running = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-2", Mode = "full_resync", Trigger = "manual" }, token);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.NotNull(problem);
        Assert.Contains(running.ToString(), problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostJobs_WithoutToken_Returns401()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client);

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-1", Mode = "routine", Trigger = "scheduled" });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("re-pair", problem!.Recovery);
    }

    [Fact]
    public async Task PostJobs_UnknownMode_Returns422()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var (status, _) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-1", Mode = "turbo", Trigger = "manual" }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact]
    public async Task PostJobs_RoutineStartCarriesKnownBookFingerprint()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "The  Pragmatic  Programmer", 2)]);
        await CompleteAsync(client, token, jobId, booksTotal: 1);

        var (resyncStatus, routine) = await SendAsync<SyncJobStartResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-2", Mode = "routine", Trigger = "scheduled" }, token);

        Assert.Equal(HttpStatusCode.OK, resyncStatus);
        Assert.NotNull(routine);
        Assert.NotEqual(jobId, routine!.JobId);

        // The same routine start replays, so the fingerprint comes back unchanged.
        var (_, replay) = await SendAsync<SyncJobStartResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-2", Mode = "routine", Trigger = "scheduled" }, token);

        Assert.NotNull(replay);
        Assert.Equal(routine.JobId, replay!.JobId);
        var book = Assert.Single(replay.KnownBooks);
        Assert.Equal("the pragmatic programmer", book.Key);
        Assert.Equal(2, book.HighlightCount);
    }

    [Fact]
    public async Task PostJobs_FullResyncCarriesNoKnownBookFingerprint()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 1)]);
        await CompleteAsync(client, token, jobId, booksTotal: 1);

        // A full resync deliberately re-reads every book, so no fingerprint is returned for the
        // extension to skip against.
        var (_, resync) = await SendAsync<SyncJobStartResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-2", Mode = "full_resync", Trigger = "manual" }, token);

        Assert.NotNull(resync);
        Assert.Empty(resync!.KnownBooks);
    }

    [Fact]
    public async Task PostBatches_ReplayReturnsTheStoredCountsUnchanged()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        var first = await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);
        Assert.False(first.Replayed);
        Assert.Equal(2, first.NewHighlights);
        Assert.Equal(1, first.NewBooks);
        Assert.Equal(0, first.DuplicateHighlights);

        var replay = await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);
        Assert.True(replay.Replayed);
        Assert.Equal(first.NewHighlights, replay.NewHighlights);
        Assert.Equal(first.NewBooks, replay.NewBooks);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlights"));

        var batches = await db.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sync_job_batches WHERE job_id = @JobId",
            new { JobId = jobId });
        Assert.Equal(1, batches);
    }

    [Fact]
    public async Task PostBatches_ReobservedExternalIdIsDuplicateEvenWhenTextChanged()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var first = await StartJobAsync(client, token, "key-1", "routine", "initial");
        var committed = await CommitBatchAsync(client, token, first, batchIndex: 0, [("B00EXAMPLE", "Dune", 1)]);
        Assert.Equal(1, committed.NewHighlights);
        await CompleteAsync(client, token, first, booksTotal: 1);

        var second = await StartJobAsync(client, token, "key-2", "routine", "scheduled");
        var (status, body) = await SendBatchAsync(client, token, second, batchIndex: 0,
        [
            new BatchBook
            {
                ExternalKey = "B00EXAMPLE",
                Title = "Dune",
                Highlights =
                [
                    new BatchHighlight { ExternalId = "B00EXAMPLE-0", Text = "Highlight 0 of Dune, reworded upstream" }
                ]
            }
        ]);

        // A re-observed highlight is matched by external id before the text dedup, so a text edit
        // upstream never produces a second highlight.
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal(0, body!.NewHighlights);
        Assert.Equal(1, body.DuplicateHighlights);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlights"));
    }

    [Fact]
    public async Task PostBatches_RecordsProvenanceAndRefreshesLastSeenOnReobservation()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);

        var first = await StartJobAsync(client, token, "key-1", "routine", "initial");
        var (firstStatus, _) = await SendBatchAsync(client, token, first, batchIndex: 0,
        [
            new BatchBook
            {
                ExternalKey = "B00EXAMPLE",
                Title = "Dune",
                Highlights =
                [
                    new BatchHighlight
                    {
                        ExternalId = "B00EXAMPLE-0",
                        Text = "A beginning is a very delicate time",
                        Note = "why I saved this",
                        Location = "10-12",
                        Color = "yellow"
                    }
                ]
            }
        ]);

        Assert.Equal(HttpStatusCode.OK, firstStatus);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        var recorded = await db.QuerySingleAsync<ProvenanceRow>(
            "SELECT highlight_id AS HighlightId, connection_id AS ConnectionId, source_id AS SourceId, region_id AS RegionId, external_id AS ExternalId, truncated AS Truncated, location AS Location, note AS Note, color AS Color, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt FROM highlight_provenance");

        Assert.Equal("kindle-cloud", recorded.SourceId);
        Assert.Equal("us", recorded.RegionId);
        Assert.Equal("B00EXAMPLE-0", recorded.ExternalId);
        Assert.Equal(0, recorded.Truncated);
        Assert.Equal("10-12", recorded.Location);
        Assert.Equal("why I saved this", recorded.Note);
        Assert.Equal("yellow", recorded.Color);
        Assert.NotNull(recorded.ConnectionId);
        Assert.Equal(recorded.FirstSeenAt, recorded.LastSeenAt);

        await CompleteAsync(client, token, first, booksTotal: 1);

        var second = await StartJobAsync(client, token, "key-2", "routine", "scheduled");
        var (secondStatus, _) = await SendBatchAsync(client, token, second, batchIndex: 0,
        [
            new BatchBook
            {
                ExternalKey = "B00EXAMPLE",
                Title = "Dune",
                Highlights =
                [
                    new BatchHighlight
                    {
                        ExternalId = "B00EXAMPLE-0",
                        Text = "A beginning is a very delicate time",
                        Note = "updated note",
                        Location = "10-12",
                        Color = "yellow"
                    }
                ]
            }
        ]);

        Assert.Equal(HttpStatusCode.OK, secondStatus);

        var refreshed = await db.QuerySingleAsync<ProvenanceRow>(
            "SELECT highlight_id AS HighlightId, connection_id AS ConnectionId, source_id AS SourceId, region_id AS RegionId, external_id AS ExternalId, truncated AS Truncated, location AS Location, note AS Note, color AS Color, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt FROM highlight_provenance");

        Assert.Equal(recorded.HighlightId, refreshed.HighlightId);
        Assert.Equal(recorded.FirstSeenAt, refreshed.FirstSeenAt);
        Assert.Equal("updated note", refreshed.Note);
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlight_provenance"));
    }

    [Fact]
    public async Task PostBatches_OversizeBody_Returns413()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        var padding = new string('x', (2 * 1024 * 1024) + 64);
        var body = new StringContent(
            $"{{\"batchIndex\":0,\"books\":[{{\"externalKey\":\"B00\",\"title\":\"{padding}\",\"highlights\":[]}}]}}",
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/batches")
        {
            Content = body
        };
        request.Headers.Add("Authorization", $"Bearer {token}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostComplete_MismatchedCountsNeverReportsSuccess()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 1)]);

        var (status, _) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/complete", new SyncJobCompleteRequest { BooksTotal = 5, BooksDone = 3, TruncatedBooks = 0 }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var jobStatus = await db.QuerySingleAsync<string>(
            "SELECT status FROM sync_jobs WHERE id = @JobId",
            new { JobId = jobId });
        Assert.Equal("running", jobStatus);

        var completedAt = await db.QuerySingleAsync<string?>(
            "SELECT last_sync_completed_at FROM sync_connections WHERE provider_id = @Provider",
            new { Provider });
        Assert.Null(completedAt);

        var job = await CompleteAsync(client, token, jobId, booksTotal: 1);
        Assert.Equal("completed_with_changes", job.Status);
        Assert.Equal(1, job.HighlightsNew);
    }

    [Fact]
    public async Task PostComplete_NoNewHighlightsReportsCompletedNoChanges()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        var job = await CompleteAsync(client, token, jobId, booksTotal: 0);

        Assert.Equal("completed_no_changes", job.Status);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("completed", summary!.Status);
        Assert.Equal(jobId, summary.LastCompletedJob?.Id);
        Assert.Null(summary.CurrentJob);
    }

    [Fact]
    public async Task PostProgress_InvalidPhase_Returns422()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        var (invalid, _) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/progress", new SyncJobProgressRequest { Phase = "not-a-phase", BooksTotal = 10, BooksDone = 2 }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid);

        var (valid, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/progress", new SyncJobProgressRequest { Phase = "reading", BooksTotal = 10, BooksDone = 2 }, token);

        Assert.Equal(HttpStatusCode.NoContent, valid);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("syncing", summary!.Status);
        Assert.Equal("reading", summary.CurrentJob?.Phase);
        Assert.Equal(2, summary.CurrentJob?.BooksDone);
    }

    [Fact]
    public async Task PostFail_RejectsDiagnosticsCarryingPageFreeText()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        var (status, problem) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/fail", new SyncJobFailRequest
        {
            Code = "provider_contract_changed",
            Diagnostics = new SyncFailureDiagnostics
            {
                ProfileVersion = "1",
                Check = "no highlight rows found while the book reported 12 highlights"
            }
        }, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("diagnostics", problem!.Errors.Keys);
    }

    [Fact]
    public async Task PostFail_AuthExpiredSurfacesRecoveryStepsAndStatus()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        var (status, _) = await SendAsync<object>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/fail", new SyncJobFailRequest
        {
            Code = "auth_expired",
            Diagnostics = new SyncFailureDiagnostics
            {
                ProfileVersion = "1",
                Check = "sign-in-form",
                Counts = new Dictionary<string, int> { ["rows"] = 0 }
            }
        }, token);

        Assert.Equal(HttpStatusCode.NoContent, status);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("auth_expired", summary!.Status);
        Assert.Equal("auth_expired", summary.Failure?.Code);
        Assert.NotEmpty(summary.Failure!.RecoverySteps);
        Assert.Contains(
            summary.Failure.RecoverySteps,
            step => step.Contains("sign in", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(summary.Failure.Detail));
    }

    [Fact]
    public async Task DeleteConnection_RevokesTokenAndKeepsHighlights()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client);
        var jobId = await StartJobAsync(client, token, "key-1", "routine", "initial");

        await CommitBatchAsync(client, token, jobId, batchIndex: 0, [("B00EXAMPLE", "Dune", 2)]);
        await CompleteAsync(client, token, jobId, booksTotal: 1);

        var (deleted, _) = await SendAsync<object>(client, HttpMethod.Delete, $"/sync/{Provider}/connection");

        Assert.Equal(HttpStatusCode.NoContent, deleted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlights"));
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM books"));
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM authors"));

        // Invariants 3 and 4: the revoked token stops working and nothing was deleted.
        var (rejected, _) = await SendAsync<ProblemPayload>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = "key-2", Mode = "routine", Trigger = "scheduled" }, token);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("not_connected", summary!.Status);
        Assert.Equal("not_connected", summary.Connection.State);
    }

    [Fact]
    public async Task GetStatus_NotConnectedStillReturnsDisclosuresAndPrerequisites()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(summary);
        Assert.Equal("not_connected", summary!.Status);
        Assert.Null(summary.Connection.Region);
        Assert.Equal("1", summary.Disclosure.Version);
        Assert.NotEmpty(summary.Prerequisites);
        Assert.Equal(360, summary.Schedule.IntervalMinutes);
        Assert.Equal(10, summary.Schedule.AllowedIntervalMinutes.Count);
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

    private static async Task<(HttpStatusCode Status, SyncBatchResponse? Body)> SendBatchAsync(
        HttpClient client,
        string token,
        long jobId,
        int batchIndex,
        IReadOnlyList<BatchBook> books) =>
        await SendAsync<SyncBatchResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/jobs/{jobId}/batches",
            new { batchIndex, books },
            token);

    private sealed record BatchBook
    {
        public string ExternalKey { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string? Author { get; init; } = "Ada Lovelace";

        public bool Truncated { get; init; }

        public IReadOnlyList<BatchHighlight> Highlights { get; init; } = [];
    }

    private sealed record BatchHighlight
    {
        public string ExternalId { get; init; } = string.Empty;

        public string Text { get; init; } = string.Empty;

        public string? Note { get; init; }

        public string? Location { get; init; }

        public string? Color { get; init; }
    }

    private sealed record ProblemPayload
    {
        public string? Detail { get; init; }

        public string? Recovery { get; init; }

        public Dictionary<string, string[]> Errors { get; init; } = [];
    }

    private sealed record ProvenanceRow
    {
        public long HighlightId { get; init; }

        public long? ConnectionId { get; init; }

        public string SourceId { get; init; } = string.Empty;

        public string? RegionId { get; init; }

        public string? ExternalId { get; init; }

        public int Truncated { get; init; }

        public string? Location { get; init; }

        public string? Note { get; init; }

        public string? Color { get; init; }

        public string FirstSeenAt { get; init; } = string.Empty;

        public string? LastSeenAt { get; init; }
    }
}
