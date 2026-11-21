using System.Globalization;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Commands.Highlights;
using Relego.Cli.Infrastructure;

namespace Relego.Tests.Cli;

public sealed class HighlightsCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();
    private readonly StringWriter _output = new();
    private readonly IAnsiConsole _previousConsole = AnsiConsole.Console;

    public HighlightsCommandTests()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(_output),
        });
        AnsiConsole.Console.Profile.Width = 200;
    }

    public void Dispose()
    {
        AnsiConsole.Console = _previousConsole;
        _mockHttp.Dispose();
        _output.Dispose();
    }

    private string Output => _output.ToString();

    private string[] Lines => Output
        .Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .ToArray();

    [Fact]
    public async Task HighlightsList_RendersNoteTagsChapterPathPositionAndDate()
    {
        var addedOn = DateTimeOffset.Parse("2026-03-02T12:00:00+00:00", CultureInfo.InvariantCulture);
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", $$"""
                {
                    "total": 1,
                    "page": 1,
                    "pageSize": 50,
                    "items": [{
                        "id": 4411,
                        "bookId": 12,
                        "authorId": 3,
                        "text": "Simplicity is prerequisite for reliability.",
                        "bookTitle": "Epigrams on Programming",
                        "authorName": "Alan Perlis",
                        "note": "the whole design principle in one line",
                        "tags": ["prob", "review"],
                        "headingLevel": null,
                        "chapterId": 4409,
                        "chapterPath": ["Foundations", "The Eight Fallacies"],
                        "position": "Location 1234-1236",
                        "addedOn": "{{addedOn:O}}"
                    }]
                }
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("4411  Epigrams on Programming — Alan Perlis", Output);
        Assert.Contains("❝ Simplicity is prerequisite for reliability.", Output);
        Assert.Contains("✎ the whole design principle in one line", Output);
        Assert.Contains("#prob #review", Output);
        Assert.Contains("Chapter: Foundations › The Eight Fallacies", Output);
        Assert.Contains("Location 1234-1236", Output);
        Assert.Contains(addedOn.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Output);
        Assert.Contains("1 annotation · page 1 of 1", Output);
    }

    [Fact]
    public async Task HighlightsList_NoteOnlyAnnotation_PrintsTheBlockWithAnEmptyPassage()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", """
                {
                    "total": 1,
                    "page": 1,
                    "pageSize": 50,
                    "items": [{
                        "id": 7,
                        "bookId": 12,
                        "authorId": 3,
                        "text": "",
                        "bookTitle": "Epigrams on Programming",
                        "authorName": "Alan Perlis",
                        "note": "read this again",
                        "tags": []
                    }]
                }
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("✎ read this again", Output);
        Assert.Contains("❝", Lines.Select(line => line.Trim()));
    }

    [Fact]
    public async Task HighlightsList_UnknownAuthor_PrintsTheFallback()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", """
                {
                    "total": 1,
                    "page": 1,
                    "pageSize": 50,
                    "items": [{
                        "id": 13,
                        "bookId": 12,
                        "authorId": 3,
                        "text": "Epigrams on Programming",
                        "bookTitle": "Epigrams on Programming",
                        "authorName": null,
                        "tags": []
                    }]
                }
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("Epigrams on Programming — Unknown author", Output);
        Assert.DoesNotContain("Unknown Author", Output);
    }

    [Fact]
    public async Task HighlightsList_Heading_PrintsItsLevel()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", """
                {
                    "total": 1,
                    "page": 1,
                    "pageSize": 50,
                    "items": [{
                        "id": 4409,
                        "bookId": 12,
                        "authorId": 3,
                        "text": "The Eight Fallacies",
                        "bookTitle": "Epigrams on Programming",
                        "authorName": "Alan Perlis",
                        "headingLevel": 2,
                        "chapterPath": ["Foundations", "The Eight Fallacies"],
                        "tags": []
                    }]
                }
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("⬒ heading (level 2)", Output);
        Assert.DoesNotContain("Chapter:", Output);
    }

    [Fact]
    public async Task HighlightsList_StitchedGroup_PrintsTheJoinedPassageOnceWithItsCount()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", """
                {
                    "total": 2,
                    "page": 1,
                    "pageSize": 50,
                    "items": [
                        {
                            "id": 20,
                            "bookId": 12,
                            "authorId": 3,
                            "text": "first fragment",
                            "bookTitle": "Epigrams on Programming",
                            "authorName": "Alan Perlis",
                            "tags": [],
                            "stitchGroupId": 20,
                            "stitchIndex": 0,
                            "stitchedText": "first fragment … second fragment"
                        },
                        {
                            "id": 21,
                            "bookId": 12,
                            "authorId": 3,
                            "text": "second fragment",
                            "bookTitle": "Epigrams on Programming",
                            "authorName": "Alan Perlis",
                            "tags": [],
                            "stitchGroupId": 20,
                            "stitchIndex": 1
                        }
                    ]
                }
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("❝ first fragment … second fragment", Output);
        Assert.Contains("(stitched from 2 highlights)", Output);
        Assert.Equal(1, Lines.Count(line => line.Contains("❝", StringComparison.Ordinal)));
        Assert.DoesNotContain("❝ second fragment", Output);
    }

    [Fact]
    public async Task HighlightsList_EmptyResult_PrintsNothingFoundAndExitsZero()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=50")
            .Respond("application/json", """
                {"total":0,"page":1,"pageSize":50,"items":[]}
                """);

        var exitCode = await RunHighlightsCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Contains("No annotations found.", Output);
    }

    [Fact]
    public async Task HighlightsList_ChapterWithoutBook_FailsWithTheActionableMessage()
    {
        var anyRequest = _mockHttp.When(HttpMethod.Get, "*").Respond(HttpStatusCode.OK);

        var exitCode = await RunHighlightsCommand("list", "--chapter", "unknown");

        Assert.Equal(1, exitCode);
        Assert.Contains("--chapter requires --book <id>.", Output);
        Assert.Equal(0, _mockHttp.GetMatchCount(anyRequest));
    }

    [Fact]
    public async Task HighlightsList_PassesEveryFilterToTheServer()
    {
        var handler = _mockHttp.When(
                HttpMethod.Get,
                "http://localhost:5000/highlights?page=2&pageSize=10&q=simplicity&bookId=12&chapterId=unknown&tag=prob")
            .Respond("application/json", """
                {"total":0,"page":2,"pageSize":10,"items":[]}
                """);

        var exitCode = await RunHighlightsCommand(
            "list", "--book", "12", "--chapter", "unknown", "--tag", "prob", "--search", "simplicity", "--page", "2", "--page-size", "10");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task HighlightsChapters_PrintsNestingCountsAndTheUnknownChapter()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/books/12/chapters")
            .Respond("application/json", """
                {
                    "bookId": 12,
                    "hasTableOfContents": true,
                    "chapters": [
                        {"id": 4409, "title": "Foundations", "level": 1, "parentId": null, "annotationCount": 31},
                        {"id": 4420, "title": "The Eight Fallacies", "level": 2, "parentId": 4409, "annotationCount": 9},
                        {"id": 4430, "title": "A Section", "level": 3, "parentId": 4420, "annotationCount": 1}
                    ],
                    "unknownChapter": {"id": null, "title": "Unknown chapter", "level": 0, "parentId": null, "annotationCount": 4}
                }
                """);

        var exitCode = await RunHighlightsCommand("chapters", "--book", "12");

        Assert.Equal(0, exitCode);

        var lines = Lines.ToList();
        Assert.Contains("Foundations", lines[0]);
        Assert.StartsWith("  └ The Eight Fallacies", lines[1]);
        Assert.StartsWith("    └ A Section", lines[2]);
        Assert.Contains("9 annotations", lines[1]);
        Assert.Contains("1 annotation", lines[2]);
        Assert.Contains("31 annotations", lines[0]);
        Assert.Contains("Unknown chapter", lines[3]);
        Assert.Contains("4 annotations", lines[3]);
    }

    [Fact]
    public async Task HighlightsChapters_NoTableOfContents_PrintsAnActionableLine()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/books/12/chapters")
            .Respond("application/json", """
                {
                    "bookId": 12,
                    "hasTableOfContents": false,
                    "chapters": [],
                    "unknownChapter": {"id": null, "title": "Unknown chapter", "level": 0, "parentId": null, "annotationCount": 4}
                }
                """);

        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/highlights?page=1&pageSize=1&bookId=12")
            .Respond("application/json", """
                {
                    "total": 4,
                    "page": 1,
                    "pageSize": 1,
                    "items": [{"id": 1, "bookId": 12, "text": "x", "bookTitle": "Epigrams on Programming", "tags": []}]
                }
                """);

        var exitCode = await RunHighlightsCommand("chapters", "--book", "12");

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "Epigrams on Programming has no chapters yet. Add .h1 markers to create them.",
            Output);
    }

    [Fact]
    public async Task HighlightsChapters_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/books/12/chapters")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunHighlightsCommand("chapters", "--book", "12");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunHighlightsCommand(params string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTransient(_ =>
        {
            var httpClient = _mockHttp.ToHttpClient();
            httpClient.BaseAddress = new Uri("http://localhost:5000");
            return new RelegoHttpClient(httpClient);
        });

        var registrar = new TypeRegistrar(services.BuildServiceProvider());
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("relego");
            config.AddBranch("highlights", hl =>
            {
                hl.AddCommand<HighlightsListCommand>("list");
                hl.AddCommand<HighlightsChaptersCommand>("chapters");
            });
        });

        var fullArgs = new[] { "highlights" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }
}
