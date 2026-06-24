using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using Spectre.Console.Cli;
using Relego.Cli.Commands.Config;
using Relego.Cli.Commands.Config.Email;
using Relego.Cli.Infrastructure;

namespace Relego.Tests.Cli;

public sealed class ConfigCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task ConfigSchedule_DailyWithValidTime_SendsPutWithTimezone()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"08:00","count":5,"kindleEmail":"test@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigScheduleCommand("daily", "08:00");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigSchedule_WeeklyWithValidTime_SendsPutWithTimezone()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"weekly","deliveryDay":"monday","deliveryTime":"09:00","count":5,"kindleEmail":"test@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigScheduleCommand("weekly", "09:00");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigSchedule_InvalidTime_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunConfigScheduleCommand("daily", "25:00");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigSchedule_InvalidCadence_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunConfigScheduleCommand("monthly", "08:00");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigSchedule_Show_FetchesAndDisplaysCurrentSchedule()
    {
        var handler = _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"08:00","count":5,"kindleEmail":"test@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigScheduleCommand("show");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigSchedule_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunConfigScheduleCommand("daily", "08:00");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunConfigScheduleCommand(params string[] args)
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
            config.AddBranch("config", cfg =>
            {
                cfg.AddCommand<ConfigScheduleCommand>("schedule");
            });
        });

        var fullArgs = new[] { "config", "schedule" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }

    [Fact]
    public async Task ConfigCount_ValidCount_SendsPut()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"08:00","count":10,"kindleEmail":"test@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigCountCommand("10");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigCount_Zero_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunConfigCountCommand("0");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigCount_TwentyExceedsMax_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunConfigCountCommand("20");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigCount_Show_FetchesCurrentCount()
    {
        var handler = _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"08:00","count":5,"kindleEmail":"test@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigCountCommand("show");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigCount_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunConfigCountCommand("10");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunConfigCountCommand(params string[] args)
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
            config.AddBranch("config", cfg =>
            {
                cfg.AddCommand<ConfigCountCommand>("count");
            });
        });

        var fullArgs = new[] { "config", "count" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }

    [Fact]
    public async Task ConfigShow_DisplaysAllSettings()
    {
        var handler = _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"weekly","deliveryDay":"monday","deliveryTime":"08:00","count":7,"kindleEmail":"user@kindle.com","timezone":"Europe/Rome"}
                """);

        var exitCode = await RunConfigShowCommand();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigShow_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunConfigShowCommand();

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunConfigShowCommand()
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
            config.AddBranch("config", cfg =>
            {
                cfg.AddCommand<ConfigShowCommand>("show");
            });
        });

        return await app.RunAsync(["config", "show"]);
    }
}

public sealed class ConfigEmailKindleCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task ConfigEmailKindle_ValidAddress_SendsPatchAndPrintsConfirmation()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"user_abc123@kindle.com","timezone":"UTC"}
                """);

        var exitCode = await RunCommand("user_abc123@kindle.com");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailKindle_InvalidAddress_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunCommand("not-an-email");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailKindle_EmptyString_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunCommand("   ");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailKindle_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunCommand("user@kindle.com");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunCommand(params string[] args)
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
            config.AddBranch("config", cfg =>
            {
                cfg.AddBranch("email", email =>
                {
                    email.AddCommand<ConfigEmailKindleCommand>("kindle");
                });
            });
        });

        var fullArgs = new[] { "config", "email", "kindle" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }
}

public sealed class ConfigEmailInboxCommandTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp = new();

    public void Dispose() => _mockHttp.Dispose();

    [Fact]
    public async Task ConfigEmailInbox_ValidAddress_SendsPatchAndPrintsConfirmation()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"","deliveryEmail":"user@example.com","timezone":"UTC"}
                """);

        var exitCode = await RunCommand("user@example.com");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailInbox_EmptyString_ClearsAddressWithHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", """
                {"schedule":"daily","deliveryDay":null,"deliveryTime":"18:00","count":5,"kindleEmail":"","deliveryEmail":null,"timezone":"UTC"}
                """);

        var exitCode = await RunCommand("");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailInbox_InvalidAddress_ReturnsOneWithoutHttpCall()
    {
        var handler = _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Respond("application/json", "{}");

        var exitCode = await RunCommand("not-an-email");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, _mockHttp.GetMatchCount(handler));
    }

    [Fact]
    public async Task ConfigEmailInbox_ServerUnreachable_ReturnsOne()
    {
        _mockHttp.When(HttpMethod.Patch, "http://localhost:5000/settings")
            .Throw(new HttpRequestException("Connection refused"));

        var exitCode = await RunCommand("user@example.com");

        Assert.Equal(1, exitCode);
    }

    private async Task<int> RunCommand(params string[] args)
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
            config.AddBranch("config", cfg =>
            {
                cfg.AddBranch("email", email =>
                {
                    email.AddCommand<ConfigEmailInboxCommand>("inbox");
                });
            });
        });

        var fullArgs = new[] { "config", "email", "inbox" }.Concat(args).ToArray();
        return await app.RunAsync(fullArgs);
    }
}
