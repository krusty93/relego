using System.Data;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Core.Sources;
using Relego.Server.Data;
using Relego.Server.Models;
using Relego.Server.Sync;

namespace Relego.Tests.Api;

public sealed class SyncFoundationApiTests
{
    [Fact]
    public async Task GetProviders_ReturnsKindleCloudWithCapabilities()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sync/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<List<SyncProviderSummary>>();
        Assert.NotNull(providers);

        var kindle = Assert.Single(providers!, provider => provider.ProviderId == "kindle-cloud");
        Assert.Equal("Kindle cloud", kindle.DisplayName);
        Assert.True(kindle.Capabilities.PushIngest);
        Assert.True(kindle.Capabilities.FullResync);
        Assert.True(kindle.Capabilities.ProgressReporting);
        Assert.Equal(45, kindle.InactivityReminderDays);
        Assert.Equal("1", kindle.DisclosureVersion);
    }

    [Fact]
    public async Task GetProviders_ListsStubProviderRegisteredInDiWithoutOtherChanges()
    {
        using var factory = new RelegoTestApplicationFactory(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ICloudSyncProvider, StubCloudSyncProvider>()));
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<List<SyncProviderSummary>>("/sync/providers");

        Assert.NotNull(providers);
        Assert.Contains(providers!, provider => provider.ProviderId == "kindle-cloud");
        var stub = Assert.Single(providers!, provider => provider.ProviderId == "stub-cloud");
        Assert.Equal("Stub cloud", stub.DisplayName);
        Assert.Equal(30, stub.InactivityReminderDays);
    }

    [Fact]
    public async Task Schema_NewTablesExist()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();

        var names = (await connection.QueryAsync<string>(
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('sync_connections', 'sync_jobs', 'sync_job_batches', 'highlight_provenance', 'sync_reminders')
            """)).AsList();

        Assert.Equal(5, names.Count);
        Assert.Contains("sync_connections", names);
        Assert.Contains("sync_jobs", names);
        Assert.Contains("sync_job_batches", names);
        Assert.Contains("highlight_provenance", names);
        Assert.Contains("sync_reminders", names);
    }

    [Fact]
    public async Task Repositories_RoundTripConnectionJobAndBatch()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var connections = scope.ServiceProvider.GetRequiredService<SyncConnectionRepository>();
        var jobs = scope.ServiceProvider.GetRequiredService<SyncJobRepository>();
        var provenance = scope.ServiceProvider.GetRequiredService<ProvenanceRepository>();

        var now = DateTimeOffset.UtcNow.ToString("o");
        var created = await connections.CreateAsync(1, "kindle-cloud", SyncTokenService.HashToken("pairing-token"), "1", now);

        var live = await connections.GetLiveAsync(1, "kindle-cloud");
        Assert.NotNull(live);
        Assert.Equal(created.Id, live!.Id);
        Assert.Equal("pending_pairing", live.State);
        Assert.Equal(360, live.SyncIntervalMinutes);

        var job = await jobs.InsertAsync(created.Id, 1, "idem-1", "routine", "initial", now);
        Assert.Equal("running", job.Status);

        var byKey = await jobs.GetByKeyAsync(1, "idem-1");
        Assert.NotNull(byKey);
        Assert.Equal(job.Id, byKey!.Id);
        Assert.Equal(job.Id, (await jobs.GetRunningAsync(created.Id))!.Id);

        await jobs.SaveBatchAsync(new SyncJobBatchRecord
        {
            JobId = job.Id,
            BatchIndex = 0,
            NewHighlights = 2,
            DuplicateHighlights = 1,
            ReceivedAt = now
        });

        var batch = await jobs.GetBatchAsync(job.Id, 0);
        Assert.NotNull(batch);
        Assert.Equal(2, batch!.NewHighlights);
        Assert.Equal(1, batch.DuplicateHighlights);

        Assert.Null(await provenance.FindHighlightIdAsync("kindle-cloud", "us", null));
    }

    [Fact]
    public async Task SyncTokenService_HashesTokensAndNeverStoresThemRaw()
    {
        Assert.NotEqual("pairing-token", SyncTokenService.HashToken("pairing-token"));

        using var factory = new RelegoTestApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var connections = scope.ServiceProvider.GetRequiredService<SyncConnectionRepository>();
        var tokens = scope.ServiceProvider.GetRequiredService<SyncTokenService>();

        const string rawToken = "ExamplePairingToken";
        var connection = await connections.CreateAsync(1, "kindle-cloud", SyncTokenService.HashToken(rawToken), "1", DateTimeOffset.UtcNow.ToString("o"));

        Assert.NotEqual(rawToken, connection.TokenHash);
        Assert.Equal(SyncTokenService.HashToken(rawToken), connection.TokenHash);

        var verified = await tokens.VerifyAsync(1, "kindle-cloud", rawToken);
        Assert.NotNull(verified);
        Assert.Equal(connection.Id, verified!.Id);

        Assert.Null(await tokens.VerifyAsync(1, "kindle-cloud", "wrong-token"));

        await tokens.RevokeAsync(connection.Id);
        Assert.Null(await connections.GetLiveAsync(1, "kindle-cloud"));
        Assert.Null(await tokens.VerifyAsync(1, "kindle-cloud", rawToken));
    }

    private sealed class StubCloudSyncProvider : ICloudSyncProvider
    {
        public SourceDescriptor Descriptor { get; } = new("stub-cloud", "Stub cloud");

        public CloudSyncCapabilities Capabilities => CloudSyncCapabilities.PushIngest;

        public int InactivityReminderDays => 30;

        public CoverageDisclosure Disclosure { get; } = new("1", []);

        public ParseProfile Profile { get; } = new("1", []);

        public SyncFrequencyPolicy Frequency { get; } = new() { AllowedMinutes = [60], Default = 60 };

        public ProviderNormalizationResult Normalize(CloudSyncBatch batch) => new() { NormalizedBatch = batch };

        public SyncRequest ToSyncRequest(ProviderNormalizationResult result) => new();
    }
}
