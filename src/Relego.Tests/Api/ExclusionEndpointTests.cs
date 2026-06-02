using System.Data;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class ExclusionEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExclusionEndpointTests()
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
    public async Task PostHighlightExclude_ThenGetExclusions_IncludesHighlight()
    {
        await SeedLibraryAsync();
        var highlightId = await GetHighlightIdAsync("Alpha highlight 1");

        var excludeResponse = await _client.PostAsync($"/highlights/{highlightId}/exclusions", null);

        Assert.Equal(HttpStatusCode.NoContent, excludeResponse.StatusCode);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        var excluded = Assert.Single(exclusions.Highlights);
        Assert.Equal(highlightId, excluded.Id);
        Assert.Equal("Alpha", excluded.BookTitle);
        Assert.Equal("Alpha highlight 1", excluded.Text);
    }

    [Fact]
    public async Task DeleteHighlightExclude_RemovesHighlightFromExclusions()
    {
        await SeedLibraryAsync();
        var highlightId = await GetHighlightIdAsync("Alpha highlight 1");
        await _client.PostAsync($"/highlights/{highlightId}/exclusions", null);

        var includeResponse = await _client.DeleteAsync($"/highlights/{highlightId}/exclusions");

        Assert.Equal(HttpStatusCode.NoContent, includeResponse.StatusCode);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        Assert.Empty(exclusions.Highlights);
    }

    [Fact]
    public async Task PostBookExclude_ThenGetExclusions_IncludesBookWithHighlightCount()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Beta");

        var excludeResponse = await _client.PostAsync($"/books/{bookId}/exclusions", null);

        Assert.Equal(HttpStatusCode.NoContent, excludeResponse.StatusCode);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        var excluded = Assert.Single(exclusions.Books);
        Assert.Equal(bookId, excluded.Id);
        Assert.Equal("Beta", excluded.Title);
        Assert.Equal("Author Beta", excluded.AuthorName);
        Assert.Equal(2, excluded.HighlightCount);
    }

    [Fact]
    public async Task DeleteBookExclude_RemovesBookFromExclusions()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Beta");
        await _client.PostAsync($"/books/{bookId}/exclusions", null);

        var includeResponse = await _client.DeleteAsync($"/books/{bookId}/exclusions");

        Assert.Equal(HttpStatusCode.NoContent, includeResponse.StatusCode);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        Assert.Empty(exclusions.Books);
    }

    [Fact]
    public async Task PostAuthorExclude_ThenGetExclusions_IncludesAuthorWithBookCount()
    {
        await SeedLibraryAsync();
        var authorId = await GetAuthorIdAsync("Shared Author");

        var excludeResponse = await _client.PostAsync($"/authors/{authorId}/exclusions", null);

        Assert.Equal(HttpStatusCode.NoContent, excludeResponse.StatusCode);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        var excluded = Assert.Single(exclusions.Authors);
        Assert.Equal(authorId, excluded.Id);
        Assert.Equal("Shared Author", excluded.Name);
        Assert.Equal(2, excluded.BookCount);
    }

    [Fact]
    public async Task PostHighlightExclude_ForMissingHighlight_Returns404()
    {
        var response = await _client.PostAsync("/highlights/999/exclusions", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Highlight 999 not found.", body);
    }

    [Fact]
    public async Task PostBookExclude_ForMissingBook_Returns404()
    {
        var response = await _client.PostAsync("/books/999/exclusions", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Book 999 not found.", body);
    }

    [Fact]
    public async Task GetExclusions_WithMixedExclusions_ReturnsAllCategories()
    {
        await SeedLibraryAsync();
        var highlightId = await GetHighlightIdAsync("Alpha highlight 1");
        var bookId = await GetBookIdAsync("Beta");
        var authorId = await GetAuthorIdAsync("Shared Author");

        await _client.PostAsync($"/highlights/{highlightId}/exclusions", null);
        await _client.PostAsync($"/books/{bookId}/exclusions", null);
        await _client.PostAsync($"/authors/{authorId}/exclusions", null);

        var exclusions = await _client.GetFromJsonAsync<ExclusionsResponse>("/exclusions");

        Assert.NotNull(exclusions);
        Assert.Contains(exclusions.Highlights, item => item.Id == highlightId);
        Assert.Contains(exclusions.Books, item => item.Id == bookId);
        Assert.Contains(exclusions.Authors, item => item.Id == authorId);
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
                        new SyncHighlightRequest { Text = "Alpha highlight 2" }
                    ]
                },
                new SyncBookRequest
                {
                    Title = "Beta",
                    Author = "Author Beta",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Beta highlight 1" },
                        new SyncHighlightRequest { Text = "Beta highlight 2" }
                    ]
                },
                new SyncBookRequest
                {
                    Title = "Gamma",
                    Author = "Shared Author",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Gamma highlight 1" }
                    ]
                },
                new SyncBookRequest
                {
                    Title = "Delta",
                    Author = "Shared Author",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Delta highlight 1" }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetHighlightIdAsync(string text)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<int>(
            "SELECT id FROM highlights WHERE text = @Text",
            new { Text = text });
    }

    private async Task<int> GetBookIdAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<int>(
            "SELECT id FROM books WHERE title = @Title",
            new { Title = title });
    }

    private async Task<int> GetAuthorIdAsync(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<int>(
            "SELECT id FROM authors WHERE name = @Name",
            new { Name = name });
    }
}
