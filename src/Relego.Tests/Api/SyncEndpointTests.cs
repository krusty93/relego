using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class SyncEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public SyncEndpointTests()
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
    public async Task PostSync_FreshImport_ReturnsCorrectCounts()
    {
        var request = BuildRequest(bookCount: 5, highlightsPerBook: 10);

        var response = await _client.PostAsJsonAsync("/highlights/import", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result);
        Assert.Equal(50, result.NewHighlights);
        Assert.Equal(0, result.DuplicateHighlights);
        Assert.Equal(5, result.NewBooks);
    }

    [Fact]
    public async Task PostSync_ReImportSamePayload_ReturnsDuplicates()
    {
        var request = BuildRequest(bookCount: 5, highlightsPerBook: 10);
        await _client.PostAsJsonAsync("/highlights/import", request);

        var response = await _client.PostAsJsonAsync("/highlights/import", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.NewHighlights);
        Assert.Equal(50, result.DuplicateHighlights);
    }

    [Fact]
    public async Task PostSync_ExistingAuthorNewBook_ReusesAuthor()
    {
        var firstRequest = new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book One",
                    Author = "Same Author",
                    Highlights = [new SyncHighlightRequest { Text = "First highlight" }]
                }
            ]
        };
        await _client.PostAsJsonAsync("/highlights/import", firstRequest);

        var secondRequest = new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Two",
                    Author = "Same Author",
                    Highlights = [new SyncHighlightRequest { Text = "Second highlight" }]
                }
            ]
        };
        var response = await _client.PostAsJsonAsync("/highlights/import", secondRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.NewAuthors);
        Assert.Equal(1, result.NewBooks);
        Assert.Equal(1, result.NewHighlights);
    }

    [Fact]
    public async Task PostSync_EmptyBooksList_ReturnsAllZeros()
    {
        var request = new SyncRequest { Books = [] };

        var response = await _client.PostAsJsonAsync("/highlights/import", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.NewHighlights);
        Assert.Equal(0, result.DuplicateHighlights);
        Assert.Equal(0, result.NewBooks);
        Assert.Equal(0, result.NewAuthors);
    }

    [Fact]
    public async Task PostSync_HighlightWithBlankText_Returns422WithFieldError()
    {
        var request = new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Valid Book",
                    Author = "Author",
                    Highlights = [new SyncHighlightRequest { Text = "   " }]
                }
            ]
        };

        var response = await _client.PostAsJsonAsync("/highlights/import", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("books[0].highlights[0].text", body);
    }

    [Fact]
    public async Task PostSync_NullAuthor_StoredAsUnknownAuthor()
    {
        var request = new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Authorless Book",
                    Author = null,
                    Highlights = [new SyncHighlightRequest { Text = "A highlight" }]
                }
            ]
        };

        var response = await _client.PostAsJsonAsync("/highlights/import", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result.NewHighlights);
        Assert.Equal(1, result.NewAuthors);
    }

    private static SyncRequest BuildRequest(int bookCount, int highlightsPerBook) =>
        new()
        {
            Books = Enumerable.Range(1, bookCount).Select(b => new SyncBookRequest
            {
                Title = $"Book {b}",
                Author = $"Author {b}",
                Highlights = Enumerable.Range(1, highlightsPerBook)
                    .Select(h => new SyncHighlightRequest { Text = $"Book {b} highlight {h}" })
                    .ToList()
            }).ToList()
        };
}
