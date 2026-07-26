using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class BookListEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public BookListEndpointTests()
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
    public async Task GetBooks_EmptyLibrary_ReturnsEmptyPage()
    {
        var response = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(response);
        Assert.Equal(0, response.Total);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetBooks_ReturnsTitlesAuthorsAndHighlightCounts()
    {
        await SeedLibraryAsync();

        var response = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(response);
        Assert.Equal(3, response.Total);

        var alpha = response.Items.Single(b => b.Title == "Alpha");
        Assert.Equal("Author Alpha", alpha.AuthorName);
        Assert.Equal(2, alpha.HighlightCount);
        Assert.False(alpha.Excluded);
        Assert.False(alpha.AuthorExcluded);
    }

    [Fact]
    public async Task GetBooks_SortsByTitleCaseInsensitively()
    {
        await SeedLibraryAsync();

        var response = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(response);
        Assert.Equal(["Alpha", "beta", "Gamma"], response.Items.Select(b => b.Title));
    }

    [Fact]
    public async Task GetBooks_QueryMatchesTitleOrAuthor()
    {
        await SeedLibraryAsync();

        var byTitle = await _client.GetFromJsonAsync<BooksResponse>("/books?q=gam");
        var byAuthor = await _client.GetFromJsonAsync<BooksResponse>("/books?q=Author%20Alpha");

        Assert.NotNull(byTitle);
        Assert.Equal("Gamma", Assert.Single(byTitle.Items).Title);

        Assert.NotNull(byAuthor);
        Assert.Equal(2, byAuthor.Total);
    }

    [Fact]
    public async Task GetBooks_ReflectsBookAndAuthorExclusions()
    {
        await SeedLibraryAsync();

        var books = await _client.GetFromJsonAsync<BooksResponse>("/books");
        Assert.NotNull(books);
        var alpha = books.Items.Single(b => b.Title == "Alpha");

        (await _client.PostAsync($"/books/{alpha.Id}/exclusions", null)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/authors/{alpha.AuthorId}/exclusions", null)).EnsureSuccessStatusCode();

        var after = await _client.GetFromJsonAsync<BooksResponse>("/books");
        Assert.NotNull(after);

        var updated = after.Items.Single(b => b.Title == "Alpha");
        Assert.True(updated.Excluded);
        Assert.True(updated.AuthorExcluded);

        // "Gamma" shares "Author Alpha", so it inherits the author exclusion but not the book one.
        var gamma = after.Items.Single(b => b.Title == "Gamma");
        Assert.False(gamma.Excluded);
        Assert.True(gamma.AuthorExcluded);
    }

    [Fact]
    public async Task GetBooks_Paginates()
    {
        await SeedLibraryAsync();

        var page2 = await _client.GetFromJsonAsync<BooksResponse>("/books?page=2&pageSize=2");

        Assert.NotNull(page2);
        Assert.Equal(3, page2.Total);
        Assert.Equal(2, page2.Page);
        Assert.Equal("Gamma", Assert.Single(page2.Items).Title);
    }

    [Theory]
    [InlineData("/books?page=0")]
    [InlineData("/books?pageSize=0")]
    [InlineData("/books?pageSize=501")]
    public async Task GetBooks_InvalidPaging_Returns422(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task SeedLibraryAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Alpha",
                    Author = "Author Alpha",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Alpha highlight 1" },
                        new SyncHighlightRequest { Text = "Alpha highlight 2" },
                    ],
                },
                new SyncBookRequest
                {
                    Title = "Gamma",
                    Author = "Author Alpha",
                    Highlights = [new SyncHighlightRequest { Text = "Gamma highlight 1" }],
                },
                new SyncBookRequest
                {
                    Title = "beta",
                    Author = "Author Beta",
                    Highlights = [new SyncHighlightRequest { Text = "Beta highlight 1" }],
                },
            ],
        });

        response.EnsureSuccessStatusCode();
    }
}
