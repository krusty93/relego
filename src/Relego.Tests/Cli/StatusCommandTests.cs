using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using Spectre.Console.Cli;
using Relego.Cli.Commands;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Tests.Cli;

public sealed class StatusCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task Status_NormalResponse_ReturnsZero()
    {
        var handler = _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Respond("application/json", """
                {
                    "totalHighlights": 120,
                    "totalBooks": 15,
                    "totalAuthors": 10,
                    "excludedHighlights": 2,
                    "excludedBooks": 1,
                    "excludedAuthors": 0,
                    "nextRecap": "2026-05-04T08:00:00Z",
                    "lastRecapStatus": "delivered",
                    "lastRecapError": null,
                    "kindleEmailConfigured": true
                }
                """);

        var exitCode = await RunStatusCommand();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task Status_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunStatusCommand();

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Status_UtcTimestampConvertedToLocal()
    {
        // Arrange: use a fixed UTC time — the command must parse it without throwing
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Respond("application/json", """
                {
                    "totalHighlights": 10,
                    "totalBooks": 2,
                    "totalAuthors": 1,
                    "excludedHighlights": 0,
                    "excludedBooks": 0,
                    "excludedAuthors": 0,
                    "nextRecap": "2026-05-04T08:00:00Z",
                    "lastRecapStatus": null,
                    "lastRecapError": null
                }
                """);

        // Act: command must succeed (conversion does not throw)
        var exitCode = await RunStatusCommand();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Status_KindleEmailNotConfigured_ReturnsZero()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Respond("application/json", """
                {
                    "totalHighlights": 5,
                    "totalBooks": 1,
                    "totalAuthors": 1,
                    "excludedHighlights": 0,
                    "excludedBooks": 0,
                    "excludedAuthors": 0,
                    "nextRecap": null,
                    "lastRecapStatus": null,
                    "lastRecapError": null,
                    "kindleEmailConfigured": false
                }
                """);

        var exitCode = await RunStatusCommand();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Status_FailedLastRecap_ReturnsZero()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Respond("application/json", """
                {
                    "totalHighlights": 5,
                    "totalBooks": 1,
                    "totalAuthors": 1,
                    "excludedHighlights": 0,
                    "excludedBooks": 0,
                    "excludedAuthors": 0,
                    "nextRecap": null,
                    "lastRecapStatus": "failed",
                    "lastRecapError": "SMTP connection timeout",
                    "kindleEmailConfigured": true
                }
                """);

        var exitCode = await RunStatusCommand();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Status_NoNextRecap_ReturnsZero()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/status")
            .Respond("application/json", """
                {
                    "totalHighlights": 0,
                    "totalBooks": 0,
                    "totalAuthors": 0,
                    "excludedHighlights": 0,
                    "excludedBooks": 0,
                    "excludedAuthors": 0,
                    "nextRecap": null,
                    "lastRecapStatus": null,
                    "lastRecapError": null
                }
                """);

        var exitCode = await RunStatusCommand();

        Assert.Equal(0, exitCode);
    }

    [Theory]
    [InlineData("not_connected", "Not connected")]
    [InlineData("awaiting_pairing", "Waiting for your browser")]
    [InlineData("auth_expired", "Amazon sign-in needed")]
    [InlineData("failed", "Last sync did not finish")]
    [InlineData("browser_not_reporting", "Your browser has not checked in")]
    [InlineData("reminder_due", "No recent sync")]
    [InlineData("connected", "Connected")]
    public void FormatKindleCloud_StatesUseTheWebWording(string status, string expected)
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus { Status = status });

        Assert.Contains(expected, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKindleCloud_Syncing_ShowsBookProgress()
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus
        {
            Status = "syncing",
            BooksDone = 37,
            BooksTotal = 120
        });

        Assert.Contains("Syncing now", rendered, StringComparison.Ordinal);
        Assert.Contains("(37/120 books)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKindleCloud_CompletedWithoutNewHighlights_SaysNothingNew()
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus
        {
            Status = "completed",
            HighlightsNew = 0
        });

        Assert.Contains("Last sync finished", rendered, StringComparison.Ordinal);
        Assert.Contains("nothing new", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKindleCloud_CompletedWithNewHighlights_ShowsTheCount()
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus
        {
            Status = "completed",
            HighlightsNew = 12
        });

        Assert.Contains("+12 new highlights", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKindleCloud_KnownRegion_IsAppended()
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus
        {
            Status = "syncing",
            BooksDone = 1,
            BooksTotal = 2,
            RegionId = "uk"
        });

        Assert.Contains("(1/2 books, uk)", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatKindleCloud_UnknownState_IsEscapedNotInterpreted()
    {
        var rendered = StatusCommand.FormatKindleCloud(new KindleCloudStatus { Status = "odd[red]state" });

        Assert.Equal("odd[[red]]state", rendered);
    }

    private async Task<int> RunStatusCommand()
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
            config.AddCommand<StatusCommand>("status");
        });

        return await app.RunAsync(["status"]);
    }
}
