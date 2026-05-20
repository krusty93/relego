using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Relego.Cli.Infrastructure;
using Relego.Cli.Import;
using Relego.Core.Contracts;

namespace Relego.Tests.Cli;

public sealed class ClippingsImportWorkflowTests : IDisposable
{
    private readonly string _tempDir;

    public ClippingsImportWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"relego-import-workflow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithValidFile_ReturnsSuccessAndPreservesSyncPayload()
    {
        var filePath = CreateClippingsFile(SampleClippings);
        using var handler = new CapturingHandler(_ => JsonResponse("""
            {"newHighlights":5,"duplicateHighlights":2,"newBooks":3,"newAuthors":2}
            """));
        using var harness = CreateWorkflowHarness(handler);

        var outcome = await harness.Workflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = filePath
        }, CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.ParseResult);
        Assert.NotNull(outcome.Response);
        Assert.Equal(5, outcome.Response!.NewHighlights);
        Assert.Equal(5, outcome.TotalHighlightsParsed);

        var requestBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var request = JsonSerializer.Deserialize<SyncRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(request);
        Assert.Collection(request!.Books,
            pragmatic =>
            {
                Assert.Equal("The Pragmatic Programmer", pragmatic.Title);
                Assert.Equal("David Thomas;Andrew Hunt", pragmatic.Author);
                Assert.Equal(2, pragmatic.Highlights.Count);
                Assert.Equal("Care About Your Craft", pragmatic.Highlights[0].Text);
                Assert.Equal("Think! About Your Work", pragmatic.Highlights[1].Text);
            },
            cleanCode =>
            {
                Assert.Equal("Clean Code", cleanCode.Title);
                Assert.Equal("Robert C. Martin", cleanCode.Author);
                Assert.Equal(3, cleanCode.Highlights.Count);
                Assert.Equal("Clean code is simple and direct.", cleanCode.Highlights[0].Text);
                Assert.Equal("The ratio of time spent reading versus writing is well over 10 to 1.", cleanCode.Highlights[1].Text);
                Assert.Equal("Leave the campground cleaner than you found it.", cleanCode.Highlights[2].Text);
            });
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyFile_ReturnsNoHighlightsFoundWithoutUploading()
    {
        var filePath = CreateClippingsFile(string.Empty);
        using var handler = new CapturingHandler(_ => throw new Xunit.Sdk.XunitException("Upload should not be attempted for an empty clippings file."));
        using var harness = CreateWorkflowHarness(handler);

        var outcome = await harness.Workflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = filePath
        }, CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.NoHighlightsFound, outcome.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithResolverReturningMissingFile_ReturnsFileNotFound()
    {
        using var handler = new CapturingHandler(_ => throw new Xunit.Sdk.XunitException("Upload should not be attempted for a missing clippings file."));
        using var harness = CreateWorkflowHarness(handler);

        var outcome = await harness.Workflow.ExecuteAsync(new ClippingsImportOptions
        {
            ResolvePathAsync = (_, _) => ValueTask.FromResult<string?>("/missing/My Clippings.txt")
        }, CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.FileNotFound, outcome.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenParserThrows_ReturnsParseFailed()
    {
        var filePath = CreateClippingsFile(SampleClippings);
        using var handler = new CapturingHandler(_ => throw new Xunit.Sdk.XunitException("Upload should not be attempted when parsing fails."));
        using var harness = CreateWorkflowHarness(handler);

        var outcome = await harness.Workflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = filePath,
            ParseAsync = static (_, _) => throw new InvalidDataException("boom")
        }, CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.ParseFailed, outcome.Status);
        Assert.Equal("boom", outcome.Message);
        Assert.IsType<InvalidDataException>(outcome.Error);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenServerIsUnreachable_ReturnsServerError()
    {
        var filePath = CreateClippingsFile(SampleClippings);
        using var handler = new CapturingHandler(_ => throw new HttpRequestException("Connection refused"));
        using var harness = CreateWorkflowHarness(handler);

        var outcome = await harness.Workflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = filePath
        }, CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.ServerError, outcome.Status);
        Assert.Equal("Connection refused", outcome.Message);
        Assert.IsType<HttpRequestException>(outcome.Error);
        Assert.Equal(1, handler.RequestCount);
    }

    private static WorkflowHarness CreateWorkflowHarness(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };

        return new WorkflowHarness(httpClient, new ClippingsImportWorkflow(new RelegoHttpClient(httpClient), NullLogger<ClippingsImportWorkflow>.Instance));
    }

    private string CreateClippingsFile(string content)
    {
        var filePath = Path.Combine(_tempDir, "My Clippings.txt");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class WorkflowHarness(HttpClient httpClient, ClippingsImportWorkflow workflow) : IDisposable
    {
        public ClippingsImportWorkflow Workflow { get; } = workflow;

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }

    private const string SampleClippings = """
        The Pragmatic Programmer (David Thomas;Andrew Hunt)
        - Your Highlight on Location 150-152 | Added on Monday, January 15, 2024 12:30:00 PM

        Care About Your Craft
        ==========
        The Pragmatic Programmer (David Thomas;Andrew Hunt)
        - Your Highlight on Location 200-205 | Added on Monday, January 15, 2024 1:00:00 PM

        Think! About Your Work
        ==========
        Clean Code (Robert C. Martin)
        - Your Highlight on Location 50-55 | Added on Tuesday, January 16, 2024 9:00:00 AM

        Clean code is simple and direct.
        ==========
        Clean Code (Robert C. Martin)
        - Your Highlight on Location 100-110 | Added on Tuesday, January 16, 2024 9:30:00 AM

        The ratio of time spent reading versus writing is well over 10 to 1.
        ==========
        Clean Code (Robert C. Martin)
        - Your Highlight on Location 150-160 | Added on Tuesday, January 16, 2024 10:00:00 AM

        Leave the campground cleaner than you found it.
        ==========
        """;
}
