using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

/// <summary>
/// Covers <c>GET /books/{id}/chapters</c>: reading order, nesting, descendant-inclusive counts,
/// the no-table-of-contents cases and the unknown chapter.
/// </summary>
public sealed class ChapterEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChapterEndpointTests()
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
    public async Task GetChapters_NestedHeadings_ReturnsReadingOrderWithNesting()
    {
        await ImportAsync(
            "Book With Chapters",
            new SyncHighlightRequest { Text = "A passage before any heading.", Position = "Location 10" },
            new SyncHighlightRequest
            {
                Text = "Foundations",
                Note = ".h1",
                Position = "Location 210",
                AddedOn = DateTimeOffset.Parse("2024-05-01T10:00:00Z", CultureInfo.InvariantCulture),
            },
            new SyncHighlightRequest { Text = "A passage in Foundations.", Position = "Location 300" },
            new SyncHighlightRequest { Text = "The Eight Fallacies", Note = ".h2", Position = "Location 400" },
            new SyncHighlightRequest { Text = "First fallacy passage.", Position = "Location 410" },
            new SyncHighlightRequest { Text = "Second fallacy passage.", Position = "Location 420" },
            new SyncHighlightRequest { Text = "Nine Fallacies", Note = ".h3", Position = "Location 500" },
            new SyncHighlightRequest { Text = "Third fallacy passage.", Position = "Location 510" },
            new SyncHighlightRequest { Text = "Closing", Note = ".h1", Position = "Location 600" });

        var bookId = await GetBookIdAsync("Book With Chapters");

        var result = await _client.GetFromJsonAsync<TableOfContentsResponse>($"/books/{bookId}/chapters");

        Assert.NotNull(result);
        Assert.Equal(bookId, result.BookId);
        Assert.True(result.HasTableOfContents);
        Assert.NotNull(result.Chapters);
        Assert.Equal(4, result.Chapters.Count);

        var foundations = result.Chapters[0];
        Assert.Equal("Foundations", foundations.Title);
        Assert.Equal(1, foundations.Level);
        Assert.Null(foundations.ParentId);
        Assert.True(foundations.Id.HasValue);
        Assert.Equal("Location 210", foundations.Position);
        Assert.Equal(
            DateTimeOffset.Parse("2024-05-01T10:00:00Z", CultureInfo.InvariantCulture),
            foundations.AddedOn);
        Assert.Equal(7, foundations.AnnotationCount);

        var fallacies = result.Chapters[1];
        Assert.Equal("The Eight Fallacies", fallacies.Title);
        Assert.Equal(2, fallacies.Level);
        Assert.Equal(foundations.Id, fallacies.ParentId);
        Assert.True(fallacies.Id.HasValue);
        Assert.Equal(5, fallacies.AnnotationCount);

        var nineFallacies = result.Chapters[2];
        Assert.Equal("Nine Fallacies", nineFallacies.Title);
        Assert.Equal(3, nineFallacies.Level);
        Assert.Equal(fallacies.Id, nineFallacies.ParentId);
        Assert.True(nineFallacies.Id.HasValue);
        Assert.Equal(2, nineFallacies.AnnotationCount);

        var closing = result.Chapters[3];
        Assert.Equal("Closing", closing.Title);
        Assert.Equal(1, closing.Level);
        Assert.Null(closing.ParentId);
        Assert.True(closing.Id.HasValue);
        Assert.NotEqual(foundations.Id, closing.Id);
        Assert.Equal(1, closing.AnnotationCount);

        Assert.Null(result.UnknownChapter.Id);
        Assert.Equal("Unknown chapter", result.UnknownChapter.Title);
        Assert.Equal(0, result.UnknownChapter.Level);
        Assert.Null(result.UnknownChapter.ParentId);
        Assert.Equal(1, result.UnknownChapter.AnnotationCount);
    }

    [Fact]
    public async Task GetChapters_BookWithoutHeadings_ReportsNoTableOfContents()
    {
        await ImportAsync(
            "Book Without Headings",
            new SyncHighlightRequest { Text = "First plain passage.", Position = "Location 10" },
            new SyncHighlightRequest { Text = "Second plain passage.", Position = "Location 20" });

        var bookId = await GetBookIdAsync("Book Without Headings");

        var result = await _client.GetFromJsonAsync<TableOfContentsResponse>($"/books/{bookId}/chapters");

        Assert.NotNull(result);
        Assert.Equal(bookId, result.BookId);
        Assert.False(result.HasTableOfContents);
        Assert.NotNull(result.Chapters);
        Assert.Empty(result.Chapters);

        Assert.Null(result.UnknownChapter.Id);
        Assert.Equal("Unknown chapter", result.UnknownChapter.Title);
        Assert.Equal(0, result.UnknownChapter.Level);
        Assert.Null(result.UnknownChapter.ParentId);
        Assert.Equal(2, result.UnknownChapter.AnnotationCount);
    }

    [Fact]
    public async Task GetChapters_BookWithOnlyDeeperHeadings_ReportsNoTableOfContents()
    {
        await ImportAsync(
            "Book With Deep Headings",
            new SyncHighlightRequest { Text = "A passage before any heading.", Position = "Location 10" },
            new SyncHighlightRequest { Text = "Notes", Note = ".h2", Position = "Location 20" },
            new SyncHighlightRequest { Text = "A passage under the second level.", Position = "Location 30" },
            new SyncHighlightRequest { Text = "Subnotes", Note = ".h3", Position = "Location 40" },
            new SyncHighlightRequest { Text = "A passage under the third level.", Position = "Location 50" });

        var bookId = await GetBookIdAsync("Book With Deep Headings");

        var result = await _client.GetFromJsonAsync<TableOfContentsResponse>($"/books/{bookId}/chapters");

        Assert.NotNull(result);
        Assert.Equal(bookId, result.BookId);
        Assert.False(result.HasTableOfContents);
        Assert.NotNull(result.Chapters);
        Assert.Empty(result.Chapters);

        Assert.Null(result.UnknownChapter.Id);
        Assert.Equal("Unknown chapter", result.UnknownChapter.Title);
        Assert.Equal(0, result.UnknownChapter.Level);
        Assert.Null(result.UnknownChapter.ParentId);
        Assert.Equal(1, result.UnknownChapter.AnnotationCount);
    }

    [Fact]
    public async Task GetChapters_MissingBook_Returns404()
    {
        var response = await _client.GetAsync("/books/9999/chapters");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Book 9999 not found.", body);
    }

    [Fact]
    public async Task GetChapters_SameTitleChapters_StayDistinct()
    {
        await ImportAsync(
            "Book With Duplicate Chapter Titles",
            new SyncHighlightRequest { Text = "Part One", Note = ".h1", Position = "Location 10" },
            new SyncHighlightRequest { Text = "Notes", Note = ".h2", Position = "Location 20" },
            new SyncHighlightRequest { Text = "A passage in the first Notes.", Position = "Location 30" },
            new SyncHighlightRequest { Text = "Notes", Note = ".h2", Position = "Location 40" },
            new SyncHighlightRequest { Text = "A passage in the second Notes.", Position = "Location 50" });

        var bookId = await GetBookIdAsync("Book With Duplicate Chapter Titles");

        var result = await _client.GetFromJsonAsync<TableOfContentsResponse>($"/books/{bookId}/chapters");

        Assert.NotNull(result);
        Assert.True(result.HasTableOfContents);

        var partOne = Assert.Single(result.Chapters, chapter => chapter.Level == 1);
        var notes = result.Chapters.Where(chapter => chapter.Title == "Notes").ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, chapter =>
        {
            Assert.True(chapter.Id.HasValue);
            Assert.Equal(2, chapter.Level);
            Assert.Equal(partOne.Id, chapter.ParentId);
            Assert.Equal(2, chapter.AnnotationCount);
        });
        Assert.NotEqual(notes[0].Id, notes[1].Id);
    }

    private async Task ImportAsync(string bookTitle, params SyncHighlightRequest[] highlights)
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = bookTitle,
                    Author = "Author Alpha",
                    Highlights = [.. highlights]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetBookIdAsync(string title)
    {
        var books = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(books);
        var book = Assert.Single(books.Items, item => item.Title == title);
        return book.Id;
    }
}
