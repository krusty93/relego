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
        Assert.NotNull(item.AddedOn);
        Assert.Equal(
            DateTimeOffset.Parse("2024-05-01T10:00:00Z", CultureInfo.InvariantCulture),
            item.AddedOn);
    }

    [Fact]
    public async Task GetHighlights_SourceWithoutPositionOrDate_ReturnsNullProvenance()
    {
        await ImportAsync(new SyncHighlightRequest
        {
            Text = "Courage is resistance to fear.",
            Position = null,
            AddedOn = null,
        });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        var item = Assert.Single(result.Items);
        Assert.Null(item.Position);
        Assert.Null(item.AddedOn);
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
    public async Task GetHighlights_BookWithoutAuthor_ReturnsNullAuthorName()
    {
        var importResponse = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Nameless",
                    Author = null,
                    Highlights = [new SyncHighlightRequest { Text = "A passage with no reported author." }]
                },
                new SyncBookRequest
                {
                    Title = "Book Blank Author",
                    Author = "  ",
                    Highlights = [new SyncHighlightRequest { Text = "Another passage with no reported author." }]
                }
            ]
        });

        importResponse.EnsureSuccessStatusCode();

        var highlights = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(highlights);
        Assert.Equal(2, highlights.Total);
        Assert.All(highlights.Items, item => Assert.Null(item.AuthorName));
        Assert.All(highlights.Items, item => Assert.NotEqual("Unknown Author", item.AuthorName));

        var books = await _client.GetFromJsonAsync<BooksResponse>("/books");

        Assert.NotNull(books);
        Assert.Equal(2, books.Items.Count);
        Assert.All(books.Items, book => Assert.Null(book.AuthorName));
        Assert.All(books.Items, book => Assert.NotEqual("Unknown Author", book.AuthorName));
    }

    [Fact]
    public async Task GetHighlights_TwoBooksWithoutAuthor_ShareSingleUnknownAuthorRow()
    {
        var importResponse = await _client.PostAsJsonAsync("/highlights/import", new SyncRequest
        {
            Books =
            [
                new SyncBookRequest
                {
                    Title = "Book Nameless One",
                    Author = null,
                    Highlights = [new SyncHighlightRequest { Text = "First passage with no reported author." }]
                },
                new SyncBookRequest
                {
                    Title = "Book Nameless Two",
                    Author = null,
                    Highlights = [new SyncHighlightRequest { Text = "Second passage with no reported author." }]
                }
            ]
        });

        importResponse.EnsureSuccessStatusCode();

        var firstBookId = await GetBookIdByTitleAsync("Book Nameless One");
        var secondBookId = await GetBookIdByTitleAsync("Book Nameless Two");

        Assert.NotEqual(firstBookId, secondBookId);
        Assert.Equal(2, await CountBooksAsync());
        Assert.Equal(1, await CountUnknownAuthorsAsync());

        var firstAuthorId = await GetBookAuthorIdAsync(firstBookId);
        var secondAuthorId = await GetBookAuthorIdAsync(secondBookId);

        Assert.Equal(await GetUnknownAuthorIdAsync(), firstAuthorId);
        Assert.Equal(firstAuthorId, secondAuthorId);
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

    [Fact]
    public async Task GetHighlights_StitchedPassage_ReportsGroupAndIndexOnEveryFragment()
    {
        await ImportAsync(
            new SyncHighlightRequest { Text = "The argument opens.", Note = ".c1" },
            new SyncHighlightRequest { Text = "It continues here.", Note = ".c2" });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights");

        Assert.NotNull(result);
        Assert.Equal(2, result.Items.Count);

        var first = result.Items[0];
        var second = result.Items[1];

        Assert.Equal(first.Id, first.StitchGroupId);
        Assert.Equal(first.Id, second.StitchGroupId);
        Assert.Equal(0, first.StitchIndex);
        Assert.Equal(1, second.StitchIndex);
        Assert.Equal("The argument opens. … It continues here.", first.StitchedText);
        Assert.Null(second.StitchedText);
    }

    [Fact]
    public async Task GetHighlights_StitchedPassage_KeepsStitchedTextOnFirstFragmentWhenPaged()
    {
        await ImportAsync(
            new SyncHighlightRequest { Text = "The argument opens.", Note = ".c1" },
            new SyncHighlightRequest { Text = "It continues here.", Note = ".c2" });

        var firstPage = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?pageSize=1&page=1");
        var secondPage = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?pageSize=1&page=2");

        var first = Assert.Single(firstPage!.Items);
        var second = Assert.Single(secondPage!.Items);

        Assert.Equal(first.Id, second.StitchGroupId);
        Assert.Equal(0, first.StitchIndex);
        Assert.Equal(1, second.StitchIndex);
        Assert.Equal("The argument opens. … It continues here.", first.StitchedText);
        Assert.Null(second.StitchedText);
    }

    [Fact]
    public async Task GetHighlights_FilterByTag_MatchesCaseInsensitivelyAndKeepsSpelling()
    {
        await ImportAsync(
            new SyncHighlightRequest { Text = "An upper-cased tag.", Note = ".PROB" },
            new SyncHighlightRequest { Text = "A lower-cased tag.", Note = ".prob" },
            new SyncHighlightRequest { Text = "Another tag entirely.", Note = ".review" });

        var upper = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?tag=PROB");
        var lower = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?tag=prob");

        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal(2, upper.Total);
        Assert.Equal(2, lower.Total);
        Assert.Equal(upper.Items.Select(item => item.Id), lower.Items.Select(item => item.Id));
        Assert.Equal(new[] { "PROB" }, upper.Items[0].Tags);
        Assert.Equal(new[] { "prob" }, upper.Items[1].Tags);
    }

    [Fact]
    public async Task GetHighlights_FilterByTag_MatchesWholeTagOnly()
    {
        await ImportAsync(
            new SyncHighlightRequest { Text = "A tag that contains the needle.", Note = ".probably" },
            new SyncHighlightRequest { Text = "The tag itself.", Note = ".prob" });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?tag=prob");

        var item = Assert.Single(result!.Items);
        Assert.Equal("The tag itself.", item.Text);
    }

    [Fact]
    public async Task GetHighlights_FilterByText_MatchesTagName()
    {
        await ImportAsync(
            new SyncHighlightRequest { Text = "An upper-cased tag.", Note = ".PROB" },
            new SyncHighlightRequest { Text = "A lower-cased tag.", Note = ".prob" },
            new SyncHighlightRequest { Text = "Another tag entirely.", Note = ".review" });

        var result = await _client.GetFromJsonAsync<HighlightsResponse>("/highlights?q=prob");

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Contains(item.Tags, tag => tag.Contains("prob", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetHighlights_FilterByTagNoMatch_ReturnsEmptyPage()
    {
        await ImportAsync(new SyncHighlightRequest { Text = "A tagged passage.", Note = ".prob" });

        var response = await _client.GetAsync("/highlights?tag=missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HighlightsResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
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

    private async Task<int> CountBooksAsync()
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM books";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<int> CountUnknownAuthorsAsync()
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM authors WHERE name IS NULL";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<int?> GetUnknownAuthorIdAsync()
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM authors WHERE name IS NULL";
        var value = await command.ExecuteScalarAsync();
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task<int> GetBookAuthorIdAsync(int bookId)
    {
        var connection = (SqliteConnection)_factory.Services.GetRequiredService<IDbConnection>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT author_id FROM books WHERE id = @BookId";
        command.Parameters.Add(new SqliteParameter("@BookId", bookId));
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
