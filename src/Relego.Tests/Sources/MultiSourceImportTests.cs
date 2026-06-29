using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Relego.Cli.Import;
using Relego.Cli.Infrastructure;
using Relego.Cli.Sources;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Sources;

public sealed class MultiSourceImportTests : IDisposable
{
    private const string SyncResponseJson =
        """{"newHighlights":2,"duplicateHighlights":0,"newBooks":1,"newAuthors":1}""";

    private const string KindleClippings = """
        Foundation (Isaac Asimov)
        - Your Highlight on Location 10-12 | Added on Monday, January 15, 2024 12:30:00 PM

        Violence is the last refuge of the incompetent.
        ==========
        Foundation (Isaac Asimov)
        - Your Highlight on Location 20-25 | Added on Monday, January 15, 2024 1:00:00 PM

        It is the chief characteristic of the religion of science that it works.
        ==========
        """;

    private readonly List<string> _tempDirs = [];

    private string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "relego-multisource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    // ── T033 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenOneSourceFails_StillImportsOthersAndSurfacesFailure()
    {
        var dir = NewTempDir();

        // A valid Kindle source.
        File.WriteAllText(Path.Combine(dir, "My Clippings.txt"), KindleClippings);

        // A Kobo database that resolves (file present under .kobo/) but fails on read
        // because it is not a real SQLite file — isolates the failure to that source.
        var koboDir = Path.Combine(dir, ".kobo");
        Directory.CreateDirectory(koboDir);
        File.WriteAllText(Path.Combine(koboDir, "KoboReader.sqlite"), "this is not a sqlite database");

        using var handler = new StubHandler(SyncResponseJson);
        using var httpClient = NewHttpClient(handler);

        // Kobo registered first, so the failing source is processed before the good one.
        var workflow = CreateWorkflow(httpClient, new KoboReaderSource(), new KindleClippingsSource());

        var outcome = await workflow.ExecuteAsync(
            new ClippingsImportOptions { FilePath = dir },
            CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.Succeeded, outcome.Status);
        Assert.Equal(2, outcome.SourceOutcomes.Count);

        var kobo = outcome.SourceOutcomes.Single(o => o.Source?.Id == "kobo");
        Assert.Equal(ClippingsImportStatus.ParseFailed, kobo.Status);
        Assert.False(string.IsNullOrWhiteSpace(kobo.Message));

        var kindle = outcome.SourceOutcomes.Single(o => o.Source?.Id == "kindle");
        Assert.Equal(ClippingsImportStatus.Succeeded, kindle.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    // ── T034 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_IsOrderIndependent_AcrossSourceRegistrationOrder()
    {
        var dir = NewTempDir();

        File.WriteAllText(Path.Combine(dir, "My Clippings.txt"), KindleClippings);
        var koboDir = Path.Combine(dir, ".kobo");
        Directory.CreateDirectory(koboDir);
        File.Copy(TestFixtures.KoboFixturePath(), Path.Combine(koboDir, "KoboReader.sqlite"));

        var kindleFirst = await ImportHighlightsAsync(dir, new KindleClippingsSource(), new KoboReaderSource());
        var koboFirst = await ImportHighlightsAsync(dir, new KoboReaderSource(), new KindleClippingsSource());

        Assert.Equal(kindleFirst, koboFirst);

        // Both sources contributed, regardless of order.
        Assert.Contains("Foundation\u0000Violence is the last refuge of the incompetent.", kindleFirst);
        Assert.Contains(
            "Clean Code\u0000Functions should do one thing. They should do it well. They should do it only.",
            kindleFirst);
    }

    private static async Task<IReadOnlyList<string>> ImportHighlightsAsync(string dir, params IHighlightSource[] sources)
    {
        using var handler = new StubHandler(SyncResponseJson);
        using var httpClient = NewHttpClient(handler);
        var workflow = CreateWorkflow(httpClient, sources);

        var outcome = await workflow.ExecuteAsync(
            new ClippingsImportOptions { FilePath = dir },
            CancellationToken.None);

        Assert.Equal(ClippingsImportStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.ParseResult);

        return outcome.ParseResult!.Books
            .SelectMany(book => book.Highlights.Select(h => $"{book.Title}\u0000{h.Text}"))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
    }

    private static ClippingsImportWorkflow CreateWorkflow(HttpClient httpClient, params IHighlightSource[] sources)
        => new(
            new RelegoHttpClient(httpClient),
            new HighlightSourceResolver(sources),
            NullLogger<ClippingsImportWorkflow>.Instance);

    private static HttpClient NewHttpClient(HttpMessageHandler handler)
        => new(handler)
        {
            BaseAddress = new Uri("http://localhost:5000")
        };

    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
