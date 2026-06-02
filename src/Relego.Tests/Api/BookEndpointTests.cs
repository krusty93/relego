using System.Data;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class BookEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public BookEndpointTests()
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
    public async Task PutBookTitle_ValidTitle_Returns204AndPersists()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "Alpha Renamed" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var storedTitle = await GetBookTitleAsync(bookId);
        Assert.Equal("Alpha Renamed", storedTitle);
    }

    [Fact]
    public async Task PutBookTitle_TitleWithWhitespace_TrimsAndPersists()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "  Trimmed Title  " });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var storedTitle = await GetBookTitleAsync(bookId);
        Assert.Equal("Trimmed Title", storedTitle);
    }

    [Fact]
    public async Task PutBookTitle_EmptyTitle_Returns422()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("title", body);
    }

    [Fact]
    public async Task PutBookTitle_WhitespaceOnlyTitle_Returns422()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "   " });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PutBookTitle_MissingBook_Returns404()
    {
        var response = await _client.PutAsJsonAsync(
            "/books/999/title",
            new RenameBookRequest { Title = "Anything" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Book 999 not found.", body);
    }

    [Fact]
    public async Task PutBookTitle_DuplicateTitleSameAuthor_Returns409()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        // "Alpha 2" is by the same "Author Alpha"
        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "Alpha 2" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutBookTitle_SameTitleDifferentAuthor_Returns204()
    {
        await SeedLibraryAsync();
        var alphaBookId = await GetBookIdAsync("Alpha");

        // "Beta" is by "Author Beta" — using its title on an "Author Alpha" book is fine
        var response = await _client.PutAsJsonAsync(
            $"/books/{alphaBookId}/title",
            new RenameBookRequest { Title = "Beta" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PutBookTitle_SameTitle_Returns204()
    {
        await SeedLibraryAsync();
        var bookId = await GetBookIdAsync("Alpha");

        var response = await _client.PutAsJsonAsync(
            $"/books/{bookId}/title",
            new RenameBookRequest { Title = "Alpha" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
                    Highlights = [new SyncHighlightRequest { Text = "Alpha highlight 1" }]
                },
                new SyncBookRequest
                {
                    Title = "Alpha 2",
                    Author = "Author Alpha",
                    Highlights = [new SyncHighlightRequest { Text = "Alpha 2 highlight 1" }]
                },
                new SyncBookRequest
                {
                    Title = "Beta",
                    Author = "Author Beta",
                    Highlights = [new SyncHighlightRequest { Text = "Beta highlight 1" }]
                },
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetBookIdAsync(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<int>(
            "SELECT id FROM books WHERE title = @Title",
            new { Title = title });
    }

    private async Task<string> GetBookTitleAsync(int bookId)
    {
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        return await connection.QuerySingleAsync<string>(
            "SELECT title FROM books WHERE id = @Id",
            new { Id = bookId });
    }
}
