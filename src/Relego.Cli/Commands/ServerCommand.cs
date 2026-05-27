using Microsoft.Extensions.Logging;
using Relego.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Relego.Cli.Commands;

/// <summary>
/// Base class for commands that communicate with the Relego server.
/// Provides shared error handling for HTTP failures.
/// </summary>
public abstract class ServerCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected abstract ILogger Logger { get; }

    protected int HandleServerError(HttpRequestException ex)
    {
        var serverUrl = Environment.GetEnvironmentVariable(ServerUrlValidator.EnvironmentVariableName)
            ?? ServerUrlValidator.DefaultServerUrl;
        Logger.LogError(ex, "Failed to reach server at {ServerUrl}", serverUrl);
        AnsiConsole.MarkupLine($"[red]Error:[/] Cannot reach server at [yellow]{Markup.Escape(serverUrl)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.Message)}[/]");
        AnsiConsole.MarkupLine($"[grey]Check that the server is running and {ServerUrlValidator.EnvironmentVariableName} is correct.[/]");
        return 1;
    }
}
