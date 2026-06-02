using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class StatusEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public StatusEndpointTests()
    {
        _factory = new RelegoTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetStatus_AfterSeedingData_ReturnsCorrectCounts()
    {
        var syncRequest = BuildRequest(bookCount: 10, highlightsPerBook: 10, authorCount: 5);
        var syncResponse = await _client.PostAsJsonAsync("/highlights/import", syncRequest);
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var response = await _client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(result);
        Assert.Equal(100, result.TotalHighlights);
        Assert.Equal(10, result.TotalBooks);
        Assert.Equal(5, result.TotalAuthors);
        Assert.Equal(0, result.ExcludedHighlights);
        Assert.NotNull(result.NextRecap);
    }

    [Fact]
    public async Task GetStatus_EmptyDatabase_ReturnsAllZeros()
    {
        var response = await _client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalHighlights);
        Assert.Equal(0, result.TotalBooks);
        Assert.Equal(0, result.TotalAuthors);
        Assert.Equal(0, result.ExcludedHighlights);
        Assert.Equal(0, result.ExcludedBooks);
        Assert.Equal(0, result.ExcludedAuthors);
        Assert.NotNull(result.NextRecap);
        Assert.Null(result.LastRecapStatus);
        Assert.Null(result.LastRecapError);
    }

    private static SyncRequest BuildRequest(int bookCount, int highlightsPerBook, int authorCount) =>
        new()
        {
            Books = Enumerable.Range(1, bookCount).Select(b => new SyncBookRequest
            {
                Title = $"Book {b}",
                Author = $"Author {((b - 1) % authorCount) + 1}",
                Highlights = Enumerable.Range(1, highlightsPerBook)
                    .Select(h => new SyncHighlightRequest { Text = $"Book {b} highlight {h}" })
                    .ToList()
            }).ToList()
        };

    [Fact]
    public async Task GetStatus_NextRecap_IsUtcIso8601()
    {
        var response = await _client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.NextRecap);

        var parsed = DateTimeOffset.Parse(result.NextRecap);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    [Fact]
    public async Task GetStatus_AfterTimezoneChange_NextRecapReflectsNewTimezone()
    {
        // Set timezone to UTC first
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { Timezone = "UTC" });
        var statusUtc = await _client.GetFromJsonAsync<StatusResponse>("/status");
        Assert.NotNull(statusUtc?.NextRecap);

        // Change timezone
        await _client.PatchAsJsonAsync("/settings", new UpdateSettingsRequest { Timezone = "Asia/Tokyo" });
        var statusTokyo = await _client.GetFromJsonAsync<StatusResponse>("/status");
        Assert.NotNull(statusTokyo?.NextRecap);

        // The UTC fire times should differ because delivery time is expressed in local timezone
        Assert.NotEqual(statusUtc.NextRecap, statusTokyo.NextRecap);
    }
}
