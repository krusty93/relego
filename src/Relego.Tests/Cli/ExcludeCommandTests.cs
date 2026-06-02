using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using Spectre.Console.Cli;
using Relego.Cli.Commands.Exclude;
using Relego.Cli.Infrastructure;

namespace Relego.Tests.Cli;

public sealed class ExcludeCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task ExcludeAdd_Highlight_SendsPost()
    {
        var handler = _mockHttp.When(HttpMethod.Post, "http://localhost:5000/highlights/5/exclusions")
            .Respond(HttpStatusCode.OK);

        var exitCode = await RunExcludeCommand("add", "highlight", "5");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ExcludeAdd_InvalidType_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Post, "*")
            .Respond(HttpStatusCode.OK);

        var exitCode = await RunExcludeCommand("add", "chapter", "5");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ExcludeAdd_NotFound_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:5000/highlights/999/exclusions")
            .Respond(HttpStatusCode.NotFound);

        var exitCode = await RunExcludeCommand("add", "highlight", "999");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExcludeRemove_Book_SendsDelete()
    {
        var handler = _mockHttp.When(HttpMethod.Delete, "http://localhost:5000/books/3/exclusions")
            .Respond(HttpStatusCode.OK);

        var exitCode = await RunExcludeCommand("remove", "book", "3");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ExcludeRemove_NotFound_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Delete, "http://localhost:5000/authors/99/exclusions")
            .Respond(HttpStatusCode.NotFound);

        var exitCode = await RunExcludeCommand("remove", "author", "99");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExcludeList_DisplaysGroupedTables()
    {
        var handler = _mockHttp.When(HttpMethod.Get, "http://localhost:5000/exclusions")
            .Respond("application/json", """
                {
                    "highlights": [{"id":1,"text":"Some highlight","bookTitle":"Clean Code"}],
                    "books": [{"id":2,"title":"Bad Book","authorName":"Unknown","highlightCount":5}],
                    "authors": []
                }
                """);

        var exitCode = await RunExcludeCommand("list");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ExcludeList_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/exclusions")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunExcludeCommand("list");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExcludeAdd_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:5000/highlights/5/exclusions")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunExcludeCommand("add", "highlight", "5");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunExcludeCommand(params string[] args)
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
            config.AddBranch("exclude", exc =>
            {
                exc.AddCommand<ExcludeAddCommand>("add");
                exc.AddCommand<ExcludeRemoveCommand>("remove");
                exc.AddCommand<ExcludeListCommand>("list");
            });
        });

        var fullArgs = new[] { "exclude" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }
}
