using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;

namespace Relego.Tests.Api;

public sealed class HighlightEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public HighlightEndpointTests()
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
    public async Task GetHighlights_Empty_ReturnsTotalZero()
    {
        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHighlights_SeededData_ReturnsAllItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task GetHighlights_ItemsHaveExpectedFields()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        var item = result.Items.First();
        Assert.True(item.Id > 0);
        Assert.True(item.BookId > 0);
        Assert.True(item.AuthorId > 0);
        Assert.NotEmpty(item.Text);
        Assert.NotEmpty(item.BookTitle);
        Assert.False(string.IsNullOrWhiteSpace(item.AuthorName));
        Assert.Null(item.Note);
        Assert.Empty(item.Tags);
    }

    [Fact]
    public async Task GetHighlights_NoteFromImport_RoundTripsNoteAndPosition()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = "Courage is resistance to fear.",
            Note = ".prob worth rereading",
            Position = "Location 1234-1236",
            AddedOn = DateTimeOffset.Parse("2024-05-01T10:00:00Z", CultureInfo.InvariantCulture),
        });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Equal("Courage is resistance to fear.", item.Text);
        Assert.Equal("worth rereading", item.Note);
        Assert.Equal("Location 1234-1236", item.Position);
        Assert.Equal(
            DateTimeOffset.Parse("2024-05-01T10:00:00Z", CultureInfo.InvariantCulture),
            item.AddedOn);
    }

    [Fact]
    public async Task GetHighlights_TextNeverCarriesMarkersOrLegacyPrefix()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = "Courage is resistance to fear.",
            Note = ".h2 .prob note body",
        });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        var item = Assert.Single(result!.Items);
        Assert.Equal("Courage is resistance to fear.", item.Text);
        Assert.DoesNotContain(".h2", item.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(".prob", item.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[my note]", item.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(".h2", item.Note!, StringComparison.Ordinal);
        Assert.Equal("note body", item.Note);
    }

    [Fact]
    public async Task GetHighlights_NoteOnlyAnnotation_ReturnsEmptyTextWithNote()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = string.Empty,
            Note = "a standalone thought",
        });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        var item = Assert.Single(result!.Items);
        Assert.Equal(string.Empty, item.Text);
        Assert.Equal("a standalone thought", item.Note);
        Assert.Null(item.Position);
        Assert.Null(item.AddedOn);
    }

    [Fact]
    public async Task GetHighlights_WithoutNote_ReturnsNullNoteAndEmptyTags()
    {
        await ImportAsync(new SyncHighlightRequest { Text = "A plain passage." });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        var item = Assert.Single(result!.Items);
        Assert.Null(item.Note);
        Assert.Empty(item.Tags);
        Assert.Null(item.HeadingLevel);
    }

    [Fact]
    public async Task DeleteHighlight_RemovesStoredHighlight()
    {
        await SeedHighlightsAsync();

        var beforeDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        var highlightId = beforeDelete!.Items[0].Id;

        var deleteResponse = await _client.DeleteAsync($"/highlights/{highlightId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        Assert.NotNull(afterDelete);
        Assert.Equal(beforeDelete.Total - 1, afterDelete.Total);
        Assert.DoesNotContain(afterDelete.Items, item => item.Id == highlightId);
    }

    [Fact]
    public async Task DeleteHighlight_WithSyncedProvenance_RemovesHighlightAndProvenance()
    {
        await SeedHighlightsAsync();

        var beforeDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        var highlightId = beforeDelete!.Items[0].Id;
        await SeedProvenanceAsync(highlightId);

        var deleteResponse = await _client.DeleteAsync($"/highlights/{highlightId}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        Assert.DoesNotContain(afterDelete!.Items, item => item.Id == highlightId);
        Assert.Equal(0, await CountProvenanceAsync(highlightId));
    }

    [Fact]
    public async Task DeleteHighlight_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync("/highlights/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHighlights_Pagination_ReturnsCorrectPage()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?page=2&pageSize=2");

        Assert.NotNull(result);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetHighlights_FilterByText_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=courage");

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Contains("courage", result.Items[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHighlights_FilterByBookTitle_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=Book+Alpha");

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("Book Alpha", item.BookTitle));
    }

    [Fact]
    public async Task GetHighlights_FilterByAuthor_ReturnsMatchingItems()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=Author+Beta");

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.All(result.Items, item => Assert.Equal("Author Beta", item.AuthorName));
    }

    [Fact]
    public async Task GetHighlights_FilterNoMatch_ReturnsEmptyList()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=nomatch_xyz");

        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHighlights_PageZero_Returns422()
    {
        var response = await _client.GetAsync("/highlights?page=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("page", body);
    }

    [Fact]
    public async Task GetHighlights_PageSizeZero_Returns422()
    {
        var response = await _client.GetAsync("/highlights?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pageSize", body);
    }

    [Fact]
    public async Task GetHighlights_PageSizeOver200_Returns422()
    {
        var response = await _client.GetAsync("/highlights?pageSize=201");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("pageSize", body);
    }

    [Fact]
    public async Task GetHighlights_ChapterFields_ReportHeadingAndChapterContext()
    {
        await SeedChapteredBookAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(5, result.Total);

        var heading = Assert.Single(result.Items, item => item.Text == "Foundations");
        var headingPassage = Assert.Single(result.Items, item => item.Text == "Foundations passage");
        var subheading = Assert.Single(result.Items, item => item.Text == "The Eight Fallacies");
        var subheadingPassage = Assert.Single(result.Items, item => item.Text == "The Eight Fallacies passage");
        var beforeChapter = Assert.Single(result.Items, item => item.Text == "Before the chapter");

        Assert.Equal(1, heading.HeadingLevel);
        Assert.Equal(heading.Id, heading.ChapterId);
        Assert.Equal(new[] { "Foundations" }, heading.ChapterPath);
        Assert.Equal(heading.Text, heading.ChapterPath[^1]);

        Assert.Null(headingPassage.HeadingLevel);
        Assert.Equal(heading.Id, headingPassage.ChapterId);
        Assert.Equal(new[] { "Foundations" }, headingPassage.ChapterPath);

        Assert.Equal(2, subheading.HeadingLevel);
        Assert.Equal(subheading.Id, subheading.ChapterId);
        Assert.Equal(new[] { "Foundations", "The Eight Fallacies" }, subheading.ChapterPath);

        Assert.Null(subheadingPassage.HeadingLevel);
        Assert.Equal(subheading.Id, subheadingPassage.ChapterId);
        Assert.Equal(new[] { "Foundations", "The Eight Fallacies" }, subheadingPassage.ChapterPath);

        Assert.Null(beforeChapter.HeadingLevel);
        Assert.Null(beforeChapter.ChapterId);
        Assert.Empty(beforeChapter.ChapterPath);
    }

    [Fact]
    public async Task GetHighlights_WithoutAnyHeading_ReportsNullChapterAndEmptyPath()
    {
        await ImportAsync(new SyncHighlightRequest { Text = "A plain passage." });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        var item = Assert.Single(result!.Items);
        Assert.Null(item.HeadingLevel);
        Assert.Null(item.ChapterId);
        Assert.Empty(item.ChapterPath);
    }

    [Fact]
    public async Task GetHighlights_FilterByBook_ReturnsOnlyThatBooksItems()
    {
        await SeedHighlightsAsync();

        var alphaBookId = await GetBookIdByTitleAsync("Book Alpha");
        var betaBookId = await GetBookIdByTitleAsync("Book Beta");

        var alpha = await _client.GetFromJsonAsync<HighlightsResponse>($"/highlights?bookId={alphaBookId}");
        var beta = await _client.GetFromJsonAsync<HighlightsResponse>($"/highlights?bookId={betaBookId}");

        Assert.NotNull(alpha);
        Assert.Equal(2, alpha.Total);
        Assert.Equal(2, alpha.Items.Count);
        Assert.All(alpha.Items, item => Assert.Equal(alphaBookId, item.BookId));
        Assert.DoesNotContain(alpha.Items, item => item.BookTitle == "Book Beta");

        Assert.NotNull(beta);
        Assert.Equal(1, beta.Total);
        Assert.Single(beta.Items);
        Assert.All(beta.Items, item => Assert.Equal(betaBookId, item.BookId));
    }

    [Fact]
    public async Task GetHighlights_FilterByChapterId_ReturnsChapterSection()
    {
        await SeedSingleChapterBookAsync();

        var bookId = await GetBookIdByTitleAsync("Book Single Chapter");
        var all = await _client.GetFromJsonAsync<HighlightsResponse>($"/highlights?bookId={bookId}");
        var chapterId = Assert.Single(all!.Items, item => item.HeadingLevel == 1).Id;

        var result = await _client.GetFromJsonAsync<HighlightsResponse>(
            $"/highlights?bookId={bookId}&chapterId={chapterId}");

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(chapterId, item.ChapterId));
        Assert.Contains(result.Items, item => item.HeadingLevel == 1);
        Assert.Contains(result.Items, item => item.Text == "Inside the chapter");
        Assert.DoesNotContain(result.Items, item => item.Text == "Before any chapter");
    }

    [Fact]
    public async Task GetHighlights_FilterByUnknownChapter_ReturnsAnnotationsBeforeFirstHeading()
    {
        await SeedSingleChapterBookAsync();

        var bookId = await GetBookIdByTitleAsync("Book Single Chapter");
        var all = await _client.GetFromJsonAsync<HighlightsResponse>($"/highlights?bookId={bookId}");
        var chapterId = Assert.Single(all!.Items, item => item.HeadingLevel == 1).Id;

        var chapter = await _client.GetFromJsonAsync<HighlightsResponse>(
            $"/highlights?bookId={bookId}&chapterId={chapterId}");
        var unknown = await _client.GetFromJsonAsync<HighlightsResponse>(
            $"/highlights?bookId={bookId}&chapterId=unknown");

        Assert.NotNull(chapter);
        Assert.NotNull(unknown);
        Assert.Equal(2, chapter.Total);
        Assert.Equal(1, unknown.Total);
        Assert.Equal(all.Total, chapter.Total + unknown.Total);

        var item = Assert.Single(unknown.Items);
        Assert.Equal("Before any chapter", item.Text);
        Assert.Null(item.HeadingLevel);
        Assert.Null(item.ChapterId);
        Assert.Empty(item.ChapterPath);
    }

    [Fact]
    public async Task GetHighlights_ChapterIdWithoutBook_IsIgnored()
    {
        await SeedSingleChapterBookAsync();

        var all = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");
        var chapterId = Assert.Single(all!.Items, item => item.HeadingLevel == 1).Id;

        var response = await _client.GetAsync($"/highlights?chapterId={chapterId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HighlightsResponse>();
        Assert.NotNull(result);
        Assert.Equal(all.Total, result.Total);
        Assert.Equal(all.Items.Count, result.Items.Count);
    }

    [Fact]
    public async Task GetHighlights_BookFilterNoMatch_ReturnsEmptyList()
    {
        await SeedHighlightsAsync();

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?bookId=9999");

        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHighlights_BookIdZero_Returns422()
    {
        var response = await _client.GetAsync("/highlights?bookId=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("bookId", body);
    }

    private async Task SeedProvenanceAsync(int highlightId)
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO highlight_provenance (highlight_id, source_id, external_id, first_seen_at)
            VALUES (@HighlightId, 'kindle_cloud', @ExternalId, @FirstSeenAt);
            """;
        command.Parameters.Add(new SqliteParameter("@HighlightId", highlightId));
        command.Parameters.Add(new SqliteParameter("@ExternalId", $"external-{highlightId}"));
        command.Parameters.Add(new SqliteParameter("@FirstSeenAt", DateTimeOffset.UtcNow.ToString("o")));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountProvenanceAsync(int highlightId)
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM highlight_provenance WHERE highlight_id = @HighlightId";
        command.Parameters.Add(new SqliteParameter("@HighlightId", highlightId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task ImportAsync(params SyncHighlightRequest[] highlights)
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Alpha",
                    Author = "Author Alpha",
                    Highlights = [.. highlights]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task SeedHighlightsAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Alpha",
                    Author = "Author Alpha",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "The secret of getting ahead is getting started." },
                        new SyncHighlightRequest { Text = "Courage is resistance to fear." }
                    ]
                },
                new SyncBookRequest
                {
                    Title = "Book Beta",
                    Author = "Author Beta",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "In the middle of every difficulty lies opportunity." }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task SeedChapteredBookAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Chapters",
                    Author = "Author Chapters",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Before the chapter" },
                        new SyncHighlightRequest { Text = "Foundations", Note = ".h1" },
                        new SyncHighlightRequest { Text = "Foundations passage" },
                        new SyncHighlightRequest { Text = "The Eight Fallacies", Note = ".h2" },
                        new SyncHighlightRequest { Text = "The Eight Fallacies passage" }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task SeedSingleChapterBookAsync()
    {
        var response = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Single Chapter",
                    Author = "Author Single Chapter",
                    Highlights =
                    [
                        new SyncHighlightRequest { Text = "Before any chapter" },
                        new SyncHighlightRequest { Text = "Only Chapter", Note = ".h1" },
                        new SyncHighlightRequest { Text = "Inside the chapter" }
                    ]
                }
            ]
        });

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetBookIdByTitleAsync(string title)
    {
        var books = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(books);
        var book = Assert.Single(books.Items, item => item.Title == title);
        return book.Id;
    }
}
