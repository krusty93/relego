using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Commands;
using Relego.Cli.Commands.Config;
using Relego.Cli.Commands.Exclude;
using Relego.Cli.Commands.Recap;
using Relego.Cli.Commands.Weight;
using Relego.Cli.Infrastructure;
using Relego.Cli.Import;
using Relego.Cli.Tui;

var builder = Host.CreateApplicationBuilder(args);

bool isTuiMode = TuiModeDetector.Detect(args, Console.IsInputRedirected) == StartupMode.Tui;

Log.Logger = isTuiMode
    ? new LoggerConfiguration()
        .MinimumLevel.Fatal()
        .CreateLogger()
    : new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddSerilog(Log.Logger, dispose: true);
});

var validationResult = ServerUrlValidator.Resolve(
    builder.Configuration[ServerUrlValidator.ConfigKey],
    Environment.GetEnvironmentVariable(ServerUrlValidator.EnvironmentVariableName),
    out Uri? serverUri,
    out string resolvedServerUrl);

if (validationResult == ServerUrlValidator.ValidationResult.Malformed)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ServerUrlValidator.EnvironmentVariableName} or {ServerUrlValidator.ConfigKey} must be a valid HTTP URL: [yellow]{Markup.Escape(resolvedServerUrl)}[/]");
    return 1;
}

var normalizedServerUrl = serverUri!.ToString().TrimEnd('/');
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    [ServerUrlValidator.ConfigKey] = normalizedServerUrl
});
Environment.SetEnvironmentVariable(ServerUrlValidator.EnvironmentVariableName, normalizedServerUrl);

Assembly assembly = typeof(ImportCommand).Assembly;
string applicationName = "relego";
string version = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString()
    ?? "unknown")
    .Split('+')[0];

builder.Services.AddHttpClient<RelegoHttpClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var serverUrl = config[ServerUrlValidator.ConfigKey]
        ?? throw new InvalidOperationException("Missing Relego server URL configuration.");
    client.BaseAddress = new Uri(serverUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddRelegoResilience();
builder.Services.AddTransient<ClippingsImportWorkflow>();

using IHost host = builder.Build();

if (isTuiMode)
{
    var client = host.Services.GetRequiredService<RelegoHttpClient>();
    var syncWorkflow = host.Services.GetRequiredService<ClippingsImportWorkflow>();
    var tuiApp = new TuiApp(client, syncWorkflow, normalizedServerUrl, version);
    await tuiApp.RunAsync(CancellationToken.None);
    return 0;
}

var registrar = new TypeRegistrar(host.Services);

var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName(applicationName);
    config.SetApplicationVersion(version);

    config.AddCommand<ImportCommand>("import")
        .WithDescription("Parse and import highlights from My Clippings.txt to the server.");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Display server health and aggregate state.");

    config.AddBranch("config", cfg =>
    {
        cfg.SetDescription("Manage server settings.");
        cfg.AddCommand<ConfigScheduleCommand>("schedule")
            .WithDescription("Configure recap schedule (cadence and time).");
        cfg.AddCommand<ConfigCountCommand>("count")
            .WithDescription("Configure number of highlights per recap.");
        cfg.AddCommand<ConfigKindleEmailCommand>("kindle-email")
            .WithDescription("Set the Kindle delivery email address.");
        cfg.AddCommand<ConfigShowCommand>("show")
            .WithDescription("Display all current settings.");
    });

    config.AddBranch("exclude", exc =>
    {
        exc.SetDescription("Manage exclusions from recaps.");
        exc.AddCommand<ExcludeAddCommand>("add")
            .WithDescription("Exclude a highlight, book, or author from recaps.");
        exc.AddCommand<ExcludeRemoveCommand>("remove")
            .WithDescription("Remove an exclusion.");
        exc.AddCommand<ExcludeListCommand>("list")
            .WithDescription("List all current exclusions.");
    });

    config.AddBranch("weight", wgt =>
    {
        wgt.SetDescription("Manage highlight recap weights.");
        wgt.AddCommand<WeightSetCommand>("set")
            .WithDescription("Set the recap weight for a highlight (1–5).");
        wgt.AddCommand<WeightListCommand>("list")
            .WithDescription("List all highlights with custom weights.");
    });

    config.AddBranch("recap", r =>
    {
        r.SetDescription("Manage recaps.");
        r.AddCommand<RecapTriggerCommand>("trigger")
            .WithDescription("Trigger a recap immediately.");
    });

    config.AddCommand<RenameBookCommand>("rename-book")
        .WithDescription("Rename a book by its ID.");
});

return await app.RunAsync(args);
