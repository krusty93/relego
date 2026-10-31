using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Core.Sources;
using Relego.Server.Sync;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the region surface of User Story 4: the read-only allowlist, and the region change the
/// extension popup performs. The invariants under test are the ones the user depends on: the hosts
/// are always derived server-side (a host is never accepted as input), changing region keeps the
/// same connection, token, frequency and history, a job that was reading the old region ends as
/// interrupted rather than as a failure, and exactly one full resync is queued for the new region.
/// </summary>
public sealed class SyncRegionApiTests
{
    private const string Provider = "kindle-cloud";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] SupportedIds = ["us", "ca", "uk", "de", "fr", "it", "es", "br"];

    [Fact]
    public async Task GetRegions_ReturnsTheEightValidatedRegionsAndTheDisclosure()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync<SyncRegionsResponse>(client, HttpMethod.Get, $"/sync/{Provider}/regions");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);

        Assert.Equal(SupportedIds, body!.Regions.Select(region => region.Id));
        Assert.Equal("United States", body.Regions[0].DisplayName);
        Assert.Equal("read.amazon.com", body.Regions[0].Host);
        Assert.Equal("lesen.amazon.de", body.Regions.Single(region => region.Id == "de").Host);
        Assert.Equal("ler.amazon.com.br", body.Regions.Single(region => region.Id == "br").Host);

        // Japan and China are incompatible, Denmark, Ireland and Poland have no validated host.
        Assert.DoesNotContain(body.Regions, region => new[] { "jp", "cn", "dk", "ie", "pl" }.Contains(region.Id));
        Assert.Contains("validated hosts are supported", body.Disclosure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRegions_ServesTheAllowlistOfTheResolvedProvider()
    {
        using var factory = new RelegoTestApplicationFactory(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ICloudSyncProvider, StubCloudSyncProvider>()));
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync<SyncRegionsResponse>(client, HttpMethod.Get, "/sync/stub-cloud/regions");

        Assert.Equal(HttpStatusCode.OK, status);
        var region = Assert.Single(body!.Regions);
        Assert.Equal("stub", region.Id);
        Assert.Equal("read.stub.example", region.Host);
        Assert.Equal("Only the stub host is supported.", body.Disclosure);
    }

    [Fact]
    public async Task GetRegions_UnknownProvider_Returns404()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<SyncRegionsResponse>(client, HttpMethod.Get, "/sync/nope/regions");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task PutRegion_SwitchesRegionWithoutRePairingAndQueuesOneFullResync()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        var (status, body) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            new SyncRegionChangeRequest { RegionId = "de" },
            token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("de", body!.Region!.Id);
        Assert.Equal("Germany", body.Region.DisplayName);
        Assert.Equal("lesen.amazon.de", body.Region.Host);
        Assert.Equal("full_resync", body.PendingCommand);

        // The same token still authenticates: a region change is never a re-pair.
        var heartbeat = await HeartbeatAsync(client, token);
        Assert.Equal("full_resync", heartbeat.Command);
        Assert.Equal("de", heartbeat.Region!.Id);

        // Exactly one command is queued, and picking it up consumes it.
        var second = await HeartbeatAsync(client, token);
        Assert.Equal("none", second.Command);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("de", summary!.Connection.Region!.Id);
        Assert.Equal("active", summary.Connection.State);
        Assert.Equal("none", summary.Schedule.PendingCommand);
    }

    [Fact]
    public async Task PutRegion_KeepsConnectionTokenFrequencyAndHistory()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        var jobId = await StartJobAsync(client, token, "key-1", "routine", "scheduled");
        await CommitBatchAsync(client, token, jobId, [("B00EXAMPLE01", "The Pragmatic Programmer", 2)]);
        await CompleteAsync(client, token, jobId, 1);

        var before = await ReadConnectionAsync(factory);
        Assert.Equal("us", before.RegionId);

        var (status, _) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            new SyncRegionChangeRequest { RegionId = "uk" },
            token);

        Assert.Equal(HttpStatusCode.OK, status);

        var after = await ReadConnectionAsync(factory);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.IntervalMinutes, after.IntervalMinutes);
        Assert.Equal("uk", after.RegionId);
        Assert.NotNull(after.RegionChangedAt);
        Assert.Equal(before.LastSyncCompletedAt, after.LastSyncCompletedAt);

        // Nothing is lost by changing region: the imported highlights and the job history stay.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM highlights"));
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs"));

        var (_, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs");
        var job = Assert.Single(jobs!);
        Assert.Equal("completed_with_changes", job.Status);
        Assert.Equal("routine", job.Mode);
    }

    [Fact]
    public async Task PutRegion_InterruptsTheRunningJobBecauseItWasReadingTheOldRegion()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        await StartJobAsync(client, token, "key-1", "routine", "scheduled");

        var (status, _) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            new SyncRegionChangeRequest { RegionId = "fr" },
            token);

        Assert.Equal(HttpStatusCode.OK, status);

        var (_, jobs) = await SendAsync<List<SyncJobDto>>(client, HttpMethod.Get, $"/sync/{Provider}/jobs");
        var job = Assert.Single(jobs!);
        Assert.Equal("interrupted", job.Status);
        Assert.Equal("interrupted", job.FailureCode);
        Assert.NotEmpty(job.RecoverySteps);
        Assert.NotNull(job.EndedAt);

        // Interrupting is not a second job and not a failure: the history stays one row, and the
        // connection never claims a completed sync.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs"));
        Assert.Null(await db.QuerySingleAsync<string?>(
            "SELECT last_sync_completed_at FROM sync_connections WHERE provider_id = @Provider",
            new { Provider }));
    }

    [Fact]
    public async Task PutRegion_SameRegionTwice_LeavesOneFullResyncQueued()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (status, body) = await SendAsync<SyncRegionChangeResponse>(
                client,
                HttpMethod.Put,
                $"/sync/{Provider}/region",
                new SyncRegionChangeRequest { RegionId = "es" },
                token);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("full_resync", body!.PendingCommand);
        }

        var heartbeat = await HeartbeatAsync(client, token);
        Assert.Equal("full_resync", heartbeat.Command);
        Assert.Equal("es", heartbeat.Region!.Id);
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("cn")]
    [InlineData("dk")]
    [InlineData("ie")]
    [InlineData("pl")]
    [InlineData("com")]
    [InlineData("US")]
    [InlineData("https://lesen.amazon.de")]
    [InlineData("lesen.amazon.de")]
    [InlineData("de/notebook")]
    [InlineData("")]
    public async Task PutRegion_UnavailableOrHostShapedId_Returns422AndKeepsTheStoredRegion(string regionId)
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        var (status, problem) = await SendAsync<ProblemPayload>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            new SyncRegionChangeRequest { RegionId = regionId },
            token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("regionId", problem!.Errors.Keys);

        var detail = string.Join(" ", problem.Errors["regionId"]);
        Assert.Contains("regionId must be one of", detail, StringComparison.Ordinal);
        Assert.Contains("Hosts are never accepted", detail, StringComparison.Ordinal);

        foreach (var supported in SupportedIds)
            Assert.Contains(supported, detail, StringComparison.Ordinal);

        // A rejected change must not have moved the connection or queued anything.
        var stored = await ReadConnectionAsync(factory);
        Assert.Equal("us", stored.RegionId);

        var (_, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("us", summary!.Connection.Region!.Id);
        Assert.Equal("none", summary.Schedule.PendingCommand);
    }

    [Fact]
    public async Task PutRegion_WithoutToken_Returns401()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client, "us");

        var (status, _) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            new SyncRegionChangeRequest { RegionId = "de" });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task PutRegion_MissingBody_Returns422()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await PairAsync(client, "us");

        var (status, _) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            $"/sync/{Provider}/region",
            null,
            token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact]
    public async Task PutRegion_UnknownProvider_Returns404()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, _) = await SendAsync<SyncRegionChangeResponse>(
            client,
            HttpMethod.Put,
            "/sync/nope/region",
            new SyncRegionChangeRequest { RegionId = "de" });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task GetStatus_RegionIsNullUntilTheExtensionPairs()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await ConnectAsync(client);

        var (_, pending) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Null(pending!.Connection.Region);
        Assert.Equal("pending_pairing", pending.Connection.State);

        await PairAsync(client, "uk", token);

        var (_, paired) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.Equal("uk", paired!.Connection.Region!.Id);
        Assert.Equal("United Kingdom", paired.Connection.Region.DisplayName);
        Assert.Equal("read.amazon.co.uk", paired.Connection.Region.Host);
    }

    [Fact]
    public async Task GetStatus_UnallowlistedStoredRegion_IsReportedAsNull()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        await PairAsync(client, "us");

        // Simulates a row written by a newer or older build: the status must not expose a host the
        // current allowlist does not know, and must not fail either.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
            await db.ExecuteAsync(
                "UPDATE sync_connections SET region_id = 'jp' WHERE provider_id = @Provider",
                new { Provider });
        }

        var (status, summary) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(summary!.Connection.Region);
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

    private static async Task<string> PairAsync(HttpClient client, string regionId, string? token = null)
    {
        token ??= await ConnectAsync(client);
        var (status, _) = await SendAsync<SyncPairResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/pair",
            new SyncPairRequest { ProfileVersion = "1", RegionId = regionId },
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
        var (status, body) = await SendAsync<SyncJobStartResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs", new SyncJobStartRequest { IdempotencyKey = idempotencyKey, Mode = mode, Trigger = trigger }, token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        return body!.JobId;
    }

    private static async Task CompleteAsync(HttpClient client, string token, long jobId, int booksTotal)
    {
        var (status, _) = await SendAsync<SyncJobDto>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/complete", new SyncJobCompleteRequest { BooksTotal = booksTotal, BooksDone = booksTotal, TruncatedBooks = 0 }, token);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    private static async Task CommitBatchAsync(
        HttpClient client,
        string token,
        long jobId,
        IReadOnlyList<(string ExternalKey, string Title, int HighlightCount)> books)
    {
        var payload = new
        {
            batchIndex = 0,
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

        var (status, _) = await SendAsync<SyncBatchResponse>(client, HttpMethod.Post, $"/sync/{Provider}/jobs/{jobId}/batches", payload, token);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    private static async Task<StoredConnection> ReadConnectionAsync(RelegoTestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        return await db.QuerySingleAsync<StoredConnection>(
            """
            SELECT id             AS Id,
                   region_id      AS RegionId,
                   region_changed_at AS RegionChangedAt,
                   sync_interval_minutes AS IntervalMinutes,
                   last_sync_completed_at AS LastSyncCompletedAt
            FROM sync_connections
            WHERE provider_id = @Provider
            """,
            new { Provider });
    }

    private sealed record StoredConnection
    {
        public long Id { get; init; }

        public string? RegionId { get; init; }

        public string? RegionChangedAt { get; init; }

        public int IntervalMinutes { get; init; }

        public string? LastSyncCompletedAt { get; init; }
    }

    private sealed record ProblemPayload
    {
        public string? Detail { get; init; }

        public Dictionary<string, string[]> Errors { get; init; } = [];
    }

    private sealed class StubCloudSyncProvider : ICloudSyncProvider
    {
        private static readonly ParseProfile StubProfile = new(
            "1",
            [new KindleRegion { Id = "stub", DisplayName = "Stub land", NotebookHost = "read.stub.example", SignInHostPatterns = ["www.stub.example"] }]);

        public SourceDescriptor Descriptor { get; } = new("stub-cloud", "Stub cloud");

        public CloudSyncCapabilities Capabilities => CloudSyncCapabilities.PushIngest;

        public int InactivityReminderDays => 30;

        public CoverageDisclosure Disclosure { get; } = new("1", []);

        public IReadOnlyList<string> Prerequisites { get; } = ["Signed in"];

        public ParseProfile Profile => StubProfile;

        public string RegionDisclosure => "Only the stub host is supported.";

        public SyncFrequencyPolicy Frequency { get; } = new() { AllowedMinutes = [60], Default = 60 };

        public ProviderNormalizationResult Normalize(CloudSyncBatch batch) => new() { NormalizedBatch = batch };

        public SyncRequest ToSyncRequest(ProviderNormalizationResult result) => new();
    }
}
