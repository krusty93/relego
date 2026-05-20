using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Relego.Cli.Infrastructure;
using Relego.Cli.Import;
using Relego.Cli.Tui;

namespace Relego.Tests.Tui;

public sealed class BookListScreenTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"relego-book-screen-{Guid.NewGuid():N}");

    public BookListScreenTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _mockHttp.Dispose();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MoveSelection_DownAndUp_MovesWithinBounds()
    {
        var screen = await CreateScreenAsync();

        screen.MoveSelection(1);
        screen.MoveSelection(1);

        Assert.Equal(1, screen.SelectedIndex);

        screen.MoveSelection(-1);
        screen.MoveSelection(-1);

        Assert.Equal(0, screen.SelectedIndex);
    }

    [Fact]
    public async Task GetSelectedBook_ReturnsFirstBookByDefault()
    {
        var screen = await CreateScreenAsync();

        var selected = screen.GetSelectedBook();

        Assert.NotNull(selected);
        Assert.Equal("Foundation", selected.Title);
    }

    [Fact]
    public async Task ActivateSearch_SetsSearchModeActive()
    {
        var screen = await CreateScreenAsync();

        screen.ActivateSearch();

        Assert.True(screen.IsSearchActive);
    }

    [Fact]
    public async Task EmptyBookList_HasNoFilteredBooks()
    {
        var screen = await CreateScreenAsync(total: 0, itemsJson: "[]");

        Assert.Empty(screen.Books);
        Assert.Empty(screen.FilteredBooks);
    }

    [Fact]
    public async Task InitializeAsync_EnrichesBooksWithExclusionsAndWeights()
    {
        var screen = await CreateScreenAsync();

        var foundation = Assert.Single(screen.Books, book => book.Title == "Foundation");
        Assert.True(foundation.IsAuthorExcluded);
        Assert.False(foundation.IsBookExcluded);
        Assert.All(foundation.Highlights, highlight => Assert.False(highlight.IsExcluded));
        Assert.Contains(foundation.Highlights, highlight => highlight.Id == 1 && highlight.Weight == 5);
        Assert.Contains(foundation.Highlights, highlight => highlight.Id == 2 && highlight.Weight is null);
    }

    [Fact]
    public async Task TryHandleShortcutKey_Q_RequestsQuitConfirmation()
    {
        var screen = await CreateScreenAsync();
        ScreenResult? result = null;

        var handled = screen.TryHandleShortcutKey('q', navigate => result = navigate, null, null, null);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Equal(ScreenAction.ConfirmQuit, result!.Action);
    }

    [Fact]
    public async Task TryHandleShortcutKey_Slash_FocusesSearchField()
    {
        var screen = await CreateScreenAsync();
        var focusedSearchField = false;

        var handled = screen.TryHandleShortcutKey('/', _ => { }, null, () => focusedSearchField = true, null);

        Assert.True(handled);
        Assert.True(focusedSearchField);
    }

    [Fact]
    public async Task TryHandleShortcutKey_I_FocusesImportField()
    {
        var screen = await CreateScreenAsync();
        var focusedImportField = false;

        var handled = screen.TryHandleShortcutKey('i', _ => { }, null, null, () => focusedImportField = true);

        Assert.True(handled);
        Assert.True(focusedImportField);
    }

    [Fact]
    public async Task TryHandleShortcutKey_R_ReinitializesBooks()
    {
        const string initialItemsJson = """
            [
              {
                "id": 1,
                "text": "Psychohistory is built on large numbers.",
                "bookTitle": "Foundation",
                "authorName": "Isaac Asimov"
              }
            ]
            """;

        using var mockHttp = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);

        mockHttp.Expect(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=100")
            .Respond("application/json", $$"""
                {
                  "total": 1,
                  "page": 1,
                  "pageSize": 100,
                  "items": {{initialItemsJson}}
                }
                """);

        mockHttp.Expect(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=100")
            .Respond("application/json", """
                {
                  "total": 0,
                  "page": 1,
                  "pageSize": 100,
                  "items": []
                }
                """);

        ConfigureSupplementaryEndpoints(mockHttp);

        var releClient = CreateRelegoClient(mockHttp);
        var workflow = new ClippingsImportWorkflow(releClient, NullLogger<ClippingsImportWorkflow>.Instance);
        var screen = new BookListScreen(releClient, workflow);
        await screen.InitializeAsync(CancellationToken.None);

        var refreshedVisibleBooks = false;
        var handled = screen.TryHandleShortcutKey('r', _ => { }, () => refreshedVisibleBooks = true, null, null);

        Assert.True(handled);
        Assert.Empty(screen.Books);
        Assert.True(refreshedVisibleBooks);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task CancelImportPrompt_LeavesScreenStable()
    {
        var screen = await CreateScreenAsync();

        SetSyncPromptState(screen, syncPathInput: "/tmp/My Clippings.txt");
        screen.CancelImportPrompt();

        Assert.False(screen.IsImportPromptActive);
    }

    [Fact]
    public async Task SubmitImportAsync_WithBlankPath_SetsValidationFeedback()
    {
        var screen = await CreateScreenAsync();

        SetSyncPromptState(screen, syncPathInput: string.Empty);
        var outcome = await screen.SubmitImportAsync(string.Empty);

        Assert.Equal(ClippingsImportStatus.Cancelled, outcome.Status);
        Assert.Equal("Enter a path to My Clippings.txt or press Esc to cancel.", screen.FeedbackMessage);
        Assert.True(screen.FeedbackIsError);
        Assert.True(screen.IsImportPromptActive);
    }

    [Fact]
    public async Task SubmitImportAsync_OnSuccess_RefreshesBooksAndClosesPrompt()
    {
        using var mockHttp = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);

        mockHttp.Expect(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=100")
            .Respond("application/json", """
                {
                  "total": 0,
                  "page": 1,
                  "pageSize": 100,
                  "items": []
                }
                """);

        mockHttp.Expect(HttpMethod.Post, "http://localhost:5000/sync")
            .Respond("application/json", """
                {
                  "newHighlights": 1,
                  "duplicateHighlights": 0,
                  "newBooks": 1,
                  "newAuthors": 1
                }
                """);

        mockHttp.Expect(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=100")
            .Respond("application/json", """
                {
                  "total": 1,
                  "page": 1,
                  "pageSize": 100,
                  "items": [
                    {
                      "id": 1,
                      "bookId": 10,
                      "authorId": 7,
                      "text": "Psychohistory is built on large numbers.",
                      "bookTitle": "Foundation",
                      "authorName": "Isaac Asimov"
                    }
                  ]
                }
                """);

        ConfigureSupplementaryEndpoints(mockHttp);

        var releClient = CreateRelegoClient(mockHttp);
        var workflow = new ClippingsImportWorkflow(releClient, NullLogger<ClippingsImportWorkflow>.Instance);
        var screen = new BookListScreen(releClient, workflow);
        await screen.InitializeAsync(CancellationToken.None);

        var filePath = CreateClippingsFile();
        SetSyncPromptState(screen, detectedPath: filePath, syncPathInput: filePath);
        var outcome = await screen.SubmitImportAsync(filePath);

        Assert.Equal(ClippingsImportStatus.Succeeded, outcome.Status);
        Assert.Single(screen.Books);
        Assert.False(screen.IsImportPromptActive);
        Assert.False(screen.FeedbackIsError);
        Assert.Contains("Import complete.", screen.FeedbackMessage);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SubmitImportAsync_OnServerError_ShowsRetryableFeedback()
    {
        using var mockHttp = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        ConfigureHighlightEndpoints(mockHttp);
        ConfigureSupplementaryEndpoints(mockHttp);

        mockHttp.When(HttpMethod.Post, "http://localhost:5000/sync")
            .Throw(new HttpRequestException("Connection refused"));

        var releClient = CreateRelegoClient(mockHttp);
        var workflow = new ClippingsImportWorkflow(releClient, NullLogger<ClippingsImportWorkflow>.Instance);
        var screen = new BookListScreen(releClient, workflow);
        await screen.InitializeAsync(CancellationToken.None);

        var filePath = CreateClippingsFile();
        SetSyncPromptState(screen, detectedPath: filePath, syncPathInput: filePath);
        var outcome = await screen.SubmitImportAsync(filePath);

        Assert.Equal(ClippingsImportStatus.ServerError, outcome.Status);
        Assert.True(screen.IsImportPromptActive);
        Assert.True(screen.FeedbackIsError);
        Assert.Equal("Import failed: Connection refused", screen.FeedbackMessage);
    }

    private async Task<BookListScreen> CreateScreenAsync(int total = 3, string? itemsJson = null)
    {
        return await CreateScreenAsync(_mockHttp, total, itemsJson).ConfigureAwait(false);
    }

    private static async Task<BookListScreen> CreateScreenAsync(MockHttpMessageHandler mockHttp, int total = 3, string? itemsJson = null)
    {
        ConfigureHighlightEndpoints(mockHttp, total, itemsJson);
        ConfigureSupplementaryEndpoints(mockHttp);

        var releClient = CreateRelegoClient(mockHttp);
        var workflow = new ClippingsImportWorkflow(releClient, NullLogger<ClippingsImportWorkflow>.Instance);
        var screen = new BookListScreen(releClient, workflow);
        await screen.InitializeAsync(CancellationToken.None);
        return screen;
    }

    private static RelegoHttpClient CreateRelegoClient(MockHttpMessageHandler mockHttp)
    {
        var httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost:5000");
        return new RelegoHttpClient(httpClient);
    }

    private static void ConfigureHighlightEndpoints(MockHttpMessageHandler mockHttp, int total = 3, string? itemsJson = null)
    {
        itemsJson ??= """
            [
              {
                "id": 1,
                "bookId": 10,
                "authorId": 7,
                "text": "Psychohistory is built on large numbers.",
                "bookTitle": "Foundation",
                "authorName": "Isaac Asimov"
              },
              {
                "id": 2,
                "bookId": 10,
                "authorId": 7,
                "text": "Violence is the last refuge of the incompetent.",
                "bookTitle": "Foundation",
                "authorName": "Isaac Asimov"
              },
              {
                "id": 3,
                "bookId": 20,
                "authorId": 8,
                "text": "In a hole in the ground there lived a hobbit.",
                "bookTitle": "The Hobbit",
                "authorName": "J.R.R. Tolkien"
              }
            ]
            """;

        mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=100")
            .Respond("application/json", $$"""
                {
                  "total": {{total}},
                  "page": 1,
                  "pageSize": 100,
                  "items": {{itemsJson}}
                }
                """);
    }

    private static void ConfigureSupplementaryEndpoints(MockHttpMessageHandler mockHttp)
    {
        mockHttp.When(HttpMethod.Get, "http://localhost:5000/exclusions")
            .Respond("application/json", """
                {
                  "highlights": [],
                  "books": [],
                  "authors": [
                    {
                      "id": 7,
                      "name": "Isaac Asimov",
                      "bookCount": 1
                    }
                  ]
                }
                """);

        mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights/weights")
            .Respond("application/json", """
                [
                  {
                    "id": 1,
                    "text": "Psychohistory is built on large numbers.",
                    "bookTitle": "Foundation",
                    "weight": 5
                  }
                ]
                """);
    }

    private string CreateClippingsFile()
    {
        var filePath = Path.Combine(_tempDir, "My Clippings.txt");
        File.WriteAllText(filePath, SampleClippings);
        return filePath;
    }

    private static void SetSyncPromptState(BookListScreen screen, string? detectedPath = null, string? syncPathInput = null)
    {
        var screenType = typeof(BookListScreen);
        var toolbarModeField = screenType.GetField("_toolbarMode", BindingFlags.Instance | BindingFlags.NonPublic);
        var detectedSyncPathField = screenType.GetField("_detectedSyncPath", BindingFlags.Instance | BindingFlags.NonPublic);
        var syncPathInputField = screenType.GetField("_syncPathInput", BindingFlags.Instance | BindingFlags.NonPublic);
        var isSearchActiveField = screenType.GetField("_isSearchActive", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(toolbarModeField);
        Assert.NotNull(detectedSyncPathField);
        Assert.NotNull(syncPathInputField);
        Assert.NotNull(isSearchActiveField);

        var syncPathMode = Enum.Parse(toolbarModeField!.FieldType, "SyncPath");
        toolbarModeField.SetValue(screen, syncPathMode);
        detectedSyncPathField!.SetValue(screen, detectedPath);
        syncPathInputField!.SetValue(screen, syncPathInput ?? string.Empty);
        isSearchActiveField!.SetValue(screen, false);
    }

    private const string SampleClippings = """
        The Pragmatic Programmer (David Thomas;Andrew Hunt)
        - Your Highlight on Location 150-152 | Added on Monday, January 15, 2024 12:30:00 PM

        Care About Your Craft
        ==========
        """;
}
