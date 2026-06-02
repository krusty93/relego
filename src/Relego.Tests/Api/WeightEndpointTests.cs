using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class WeightEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public WeightEndpointTests()
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
    public async Task PutWeight_ValidWeight_PersistsAndListsHighlight()
    {
        await SeedHighlightsAsync();

        var setResponse = await _client.PutAsJsonAsync("/highlights/1/weight", new SetWeightRequest { Weight = 5 });

        Assert.Equal(HttpStatusCode.NoContent, setResponse.StatusCode);

        var weightedHighlights = await _client.GetFromJsonAsync<List<WeightedHighlightDto>>("/highlights/weights");

        Assert.NotNull(weightedHighlights);
        var weighted = Assert.Single(weightedHighlights);
        Assert.Equal(1, weighted.Id);
        Assert.Equal("Highlight one", weighted.Text);
        Assert.Equal("Weighted Book", weighted.BookTitle);
        Assert.Equal(5, weighted.Weight);
    }

    [Fact]
    public async Task PutWeight_Zero_Returns422WithFieldError()
    {
        var response = await _client.PutAsJsonAsync("/highlights/1/weight", new SetWeightRequest { Weight = 0 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("weight", body);
    }

    [Fact]
    public async Task PutWeight_Six_Returns422WithFieldError()
    {
        var response = await _client.PutAsJsonAsync("/highlights/1/weight", new SetWeightRequest { Weight = 6 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("weight", body);
    }

    [Fact]
    public async Task PutWeight_MissingHighlight_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/highlights/999/weight", new SetWeightRequest { Weight = 5 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Highlight 999 not found.", body);
    }

    [Fact]
    public async Task GetWeights_WithOnlyDefaultWeights_ReturnsEmptyList()
    {
        await SeedHighlightsAsync();

        var weightedHighlights = await _client.GetFromJsonAsync<List<WeightedHighlightDto>>("/highlights/weights");

        Assert.NotNull(weightedHighlights);
        Assert.Empty(weightedHighlights);
    }

    private async Task SeedHighlightsAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Weighted Book",
                    Author = "Author One",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Highlight one" },
                        new SyncHighlightRequest { Text = "Highlight two" }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }
}
