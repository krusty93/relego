using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using Spectre.Console.Cli;
using Relego.Cli.Commands.Recap;
using Relego.Cli.Infrastructure;

namespace Relego.Tests.Cli;

public sealed class RecapTriggerCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task RecapTrigger_NoDestinationConfigured_ReturnsOneWithoutTriggerCall()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"","deliveryEmail":null,"timezone":"UTC"}
                """);

        var triggerHandler = _mockHttp.When(HttpMethod.Post, "http://localhost:5000/recaps")
            .Respond("application/json", """{"status":"triggered","scheduledFor":"2026-01-01T18:00:00Z"}""");

        var exitCode = await RunTriggerCommand();

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(triggerHandler));
    }

    [Fact]
    public async Task RecapTrigger_KindleEmailConfigured_TriggersRecap()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"user@kindle.com","deliveryEmail":null,"timezone":"UTC"}
                """);

        var triggerHandler = _mockHttp.When(HttpMethod.Post, "http://localhost:5000/recaps")
            .Respond("application/json", """{"status":"triggered","scheduledFor":"2026-01-01T18:00:00Z"}""");

        var exitCode = await RunTriggerCommand();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(triggerHandler));
    }

    [Fact]
    public async Task RecapTrigger_InboxEmailConfigured_TriggersRecap()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"","deliveryEmail":"user@example.com","timezone":"UTC"}
                """);

        var triggerHandler = _mockHttp.When(HttpMethod.Post, "http://localhost:5000/recaps")
            .Respond("application/json", """{"status":"triggered","scheduledFor":"2026-01-01T18:00:00Z"}""");

        var exitCode = await RunTriggerCommand();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(triggerHandler));
    }

    [Fact]
    public async Task RecapTrigger_SettingsUnreachable_FallsThroughToTriggerCall()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        _mockHttp.When(HttpMethod.Post, "http://localhost:5000/recaps")
            .Respond("application/json", """{"status":"triggered","scheduledFor":"2026-01-01T18:00:00Z"}""");

        // Settings check is best-effort; when unreachable we skip the pre-check and let the trigger call succeed.
        var exitCode = await RunTriggerCommand();

        Assert.Equal(0, exitCode);
    }

    private async Task<int> RunTriggerCommand()
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
            config.AddBranch("recap", r =>
            {
                r.AddCommand<RecapTriggerCommand>("trigger");
            });
        });

        return await app.RunAsync(["recap", "trigger"]);
    }
}
