using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

/// <summary>
/// Covers <c>GET /tags</c>: case-insensitive grouping with a representative spelling, ordering by
/// count then name, and the library-wide annotation count.
/// </summary>
public sealed class TagEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public TagEndpointTests()
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
    public async Task GetTags_EmptyLibrary_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<TagsResponse>();

        Assert.NotNull(result);
        Assert.Empty(result.Tags);
    }

    [Fact]
    public async Task GetTags_SameTagInDifferentSpellings_GroupsCaseInsensitively()
    {
        await ImportAsync(
            "Book Tag Spellings",
            new SyncHighlightRequest { Text = "First tagged passage.", Note = ".PROB" },
            new SyncHighlightRequest { Text = "Second tagged passage.", Note = ".prob" },
            new SyncHighlightRequest { Text = "Third tagged passage.", Note = ".Prob .review" });

        var result = await _client.GetFromJsonAsync<TagsResponse>("/tags");

        Assert.NotNull(result);
        var prob = Assert.Single(
            result.Tags,
            tag => string.Equals(tag.Name, "prob", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("PROB", prob.Name);
        Assert.Equal(3, prob.Count);

        var review = Assert.Single(result.Tags, tag => tag.Name == "review");
        Assert.Equal(1, review.Count);
    }

    [Fact]
    public async Task GetTags_MixedCounts_OrdersByCountDescendingThenLowerCaseName()
    {
        await ImportAsync(
            "Book Tag Ordering",
            new SyncHighlightRequest { Text = "First common passage.", Note = ".common" },
            new SyncHighlightRequest { Text = "Second common passage.", Note = ".common" },
            new SyncHighlightRequest { Text = "Third common passage.", Note = ".Common" },
            new SyncHighlightRequest { Text = "An alpha passage.", Note = ".alpha" },
            new SyncHighlightRequest { Text = "A beta passage.", Note = ".Beta" });

        var result = await _client.GetFromJsonAsync<TagsResponse>("/tags");

        Assert.NotNull(result);
        Assert.Equal(3, result.Tags.Count);

        Assert.Equal("common", result.Tags[0].Name);
        Assert.Equal(3, result.Tags[0].Count);
        Assert.Equal("alpha", result.Tags[1].Name);
        Assert.Equal(1, result.Tags[1].Count);
        Assert.Equal("Beta", result.Tags[2].Name);
        Assert.Equal(1, result.Tags[2].Count);
    }

    [Fact]
    public async Task GetTags_SameTagInTwoBooks_CountsEveryAnnotation()
    {
        await ImportAsync(
            new SyncBookRequest
            {
                Title = "Book Tag Alpha",
                Author = "Author Alpha",
                Highlights = [new SyncHighlightRequest { Text = "A tagged passage in the first book.", Note = ".prob" }]
            },
            new SyncBookRequest
            {
                Title = "Book Tag Beta",
                Author = "Author Beta",
                Highlights = [new SyncHighlightRequest { Text = "A tagged passage in the second book.", Note = ".Prob" }]
            });

        var result = await _client.GetFromJsonAsync<TagsResponse>("/tags");

        Assert.NotNull(result);
        var prob = Assert.Single(result.Tags);
        Assert.Equal("prob", prob.Name);
        Assert.Equal(2, prob.Count);
    }

    private async Task ImportAsync(string bookTitle, params SyncHighlightRequest[] highlights)
    {
        await ImportAsync(new SyncBookRequest
        {
            Title = bookTitle,
            Author = "Author Alpha",
            Highlights = [.. highlights],
        });
    }

    private async Task ImportAsync(params SyncBookRequest[] books)
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books = [.. books]
        });

        response.EnsureSuccessStatusCode();
    }
}