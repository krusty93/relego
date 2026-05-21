using System.Net;
using RichardSzalay.MockHttp;
using Relego.Cli.Infrastructure;
using Relego.Cli.Tui;
using Relego.Cli.Tui.ViewModels;

namespace Relego.Tests.Tui;

public sealed class HighlightDetailScreenTests
{
    [Fact]
    public void KeyHints_ExposeDetailAndQuitWithoutCtrlC()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        Assert.Contains(screen.KeyHints, hint => hint is ("Enter", "Open details and actions"));
        Assert.DoesNotContain(screen.KeyHints, hint => hint is ("A", "Actions"));
        Assert.Contains(screen.KeyHints, hint => hint is ("Q", "Quit"));
        Assert.DoesNotContain(screen.KeyHints, hint => string.Equals(hint.Key, "Ctrl+C", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task KeyHints_ChangeWithModalState()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.Equal([("↑↓", "Navigate"), ("Enter", "Use action"), ("Tab", "Switch panel"), ("Esc", "Close")], screen.KeyHints);

        await screen.HandleKeyAsync(Key(ConsoleKey.Tab, '\t'), CancellationToken.None);
        Assert.Equal([("↑↓", "Scroll"), ("Tab", "Switch panel"), ("Esc", "Close")], screen.KeyHints);

        await screen.HandleKeyAsync(Key(ConsoleKey.Tab, '\t'), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.Equal([("↑↓", "Adjust"), ("1-5", "Set"), ("Enter", "Save"), ("Esc", "Cancel")], screen.KeyHints);
    }

    [Fact]
    public async Task RegisterUiStateObserver_NotifiesOnModalTransitions()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));
        var notifications = 0;

        screen.RegisterUiStateObserver(() => notifications++);

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Tab, '\t'), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);

        Assert.Equal(3, notifications);
    }

    [Fact]
    public async Task HandleKeyAsync_NavigatesWithinBounds()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);

        Assert.Equal(1, screen.SelectedIndex);

        await screen.HandleKeyAsync(Key(ConsoleKey.UpArrow), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.UpArrow), CancellationToken.None);

        Assert.Equal(0, screen.SelectedIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_EnterOpensDetailModal()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.True(screen.DetailOpen);
        Assert.Equal(0, screen.ActionMenuIndex);
        Assert.True(screen.DetailFocusOnActions);
        Assert.Equal("First highlight", screen.PreviewText);
    }

    [Fact]
    public async Task HandleKeyAsync_TabSwitchesFocusBetweenDetailPanes()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.True(screen.DetailFocusOnActions);

        await screen.HandleKeyAsync(Key(ConsoleKey.Tab, '\t'), CancellationToken.None);
        Assert.False(screen.DetailFocusOnActions);

        await screen.HandleKeyAsync(Key(ConsoleKey.Tab, '\t'), CancellationToken.None);
        Assert.True(screen.DetailFocusOnActions);
    }

    [Fact]
    public async Task HandleKeyAsync_EscapeClosesDetailBeforePopping()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        var firstEscape = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);
        var secondEscape = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);

        Assert.Equal(ScreenAction.None, firstEscape.Action);
        Assert.False(screen.DetailOpen);
        Assert.Equal(ScreenAction.Pop, secondEscape.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_DetailActionNavigationStaysWithinBounds()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        for (var index = 0; index < 10; index++)
        {
            await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        }

        Assert.Equal(4, screen.ActionMenuIndex);

        for (var index = 0; index < 10; index++)
        {
            await screen.HandleKeyAsync(Key(ConsoleKey.UpArrow), CancellationToken.None);
        }

        Assert.Equal(0, screen.ActionMenuIndex);
    }

    [Fact]
    public async Task HandleKeyAsync_SetWeightOpensEditorAndAppliesExplicitValue()
    {
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Put, "http://localhost/highlights/11/weight")
            .Respond(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.True(screen.WeightEditorOpen);

        await screen.HandleKeyAsync(Key(ConsoleKey.D5, '5'), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        Assert.False(screen.DetailOpen);
        Assert.False(screen.WeightEditorOpen);
        Assert.Equal(5, screen.Highlights[0].Weight);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task HandleKeyAsync_EscapeCancelsWeightEditorWithoutChangingWeight()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.D5, '5'), CancellationToken.None);

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.False(screen.WeightEditorOpen);
        Assert.Null(screen.Highlights[0].Weight);
    }

    [Fact]
    public async Task HandleKeyAsync_QWhileModalOpen_DoesNotEscapeModal()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Q, 'q'), CancellationToken.None);

        Assert.Equal(ScreenAction.None, result.Action);
        Assert.True(screen.DetailOpen);
    }

    [Fact]
    public async Task HandleKeyAsync_EnterOpensDetailAndEscapeClosesItBeforePopping()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        var showResult = await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.Equal(ScreenAction.None, showResult.Action);
        Assert.True(screen.DetailOpen);
        Assert.Equal("First highlight", screen.PreviewText);

        var firstEscape = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);
        Assert.Equal(ScreenAction.None, firstEscape.Action);
        Assert.False(screen.DetailOpen);
        Assert.Null(screen.PreviewText);

        var secondEscape = await screen.HandleKeyAsync(Key(ConsoleKey.Escape), CancellationToken.None);
        Assert.Equal(ScreenAction.Pop, secondEscape.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_QRequestsQuitConfirmation()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        var result = await screen.HandleKeyAsync(Key(ConsoleKey.Q, 'q'), CancellationToken.None);

        Assert.Equal(ScreenAction.ConfirmQuit, result.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_CtrlCQuitsScreenImmediately()
    {
        using var mockHttp = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        var result = await screen.HandleKeyAsync(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, true), CancellationToken.None);

        Assert.Equal(ScreenAction.Quit, result.Action);
    }

    [Fact]
    public async Task HandleKeyAsync_ToggleHighlightExclusionCallsApi()
    {
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Post, "http://localhost/highlights/11/exclude")
            .Respond(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);

        Assert.True(screen.Highlights[0].IsExcluded);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task HandleKeyAsync_DeleteConfirmationDeletesHighlight()
    {
        using var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Delete, "http://localhost/highlights/11")
            .Respond(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(mockHttp, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };

        var screen = CreateScreen(new RelegoHttpClient(httpClient));

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        for (var index = 0; index < 4; index++)
        {
            await screen.HandleKeyAsync(Key(ConsoleKey.DownArrow), CancellationToken.None);
        }

        await screen.HandleKeyAsync(Key(ConsoleKey.Enter), CancellationToken.None);
        Assert.True(screen.DeleteConfirmationOpen);

        await screen.HandleKeyAsync(Key(ConsoleKey.Y, 'y'), CancellationToken.None);

        Assert.False(screen.DeleteConfirmationOpen);
        Assert.Single(screen.Highlights);
        Assert.DoesNotContain(screen.Highlights, highlight => highlight.Id == 11);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    private static HighlightDetailScreen CreateScreen(RelegoHttpClient client)
        => new(
            new BookViewModel(
                101,
                201,
                "Book A",
                "Author A",
                2,
                false,
                false,
                [
                    new HighlightViewModel(11, 101, 201, "First highlight", "Book A", "Author A", false, null),
                    new HighlightViewModel(12, 101, 201, "Second highlight", "Book A", "Author A", false, 5)
                ]),
            client);

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar = '\0')
        => new(keyChar, key, shift: false, alt: false, control: false);
}
