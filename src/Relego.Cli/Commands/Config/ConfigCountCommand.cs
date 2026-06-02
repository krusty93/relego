using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config;

/// <summary>
/// Configures the number of highlights per recap on the server.
/// Usage: relego config count &lt;value&gt;   (1–15)
///        relego config count show
/// </summary>
public sealed class ConfigCountCommand(RelegoHttpClient client, ILogger<ConfigCountCommand> logger)
    : ServerCommand<ConfigCountCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<value>")]
        [Description("Number of highlights per recap (1–15), or 'show' to display current count.")]
        public string Value { get; set; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        if (settings.Value.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            return await ShowCountAsync(cancellation);
        }

        return await SetCountAsync(settings, cancellation);
    }

    private async Task<int> ShowCountAsync(CancellationToken cancellation)
    {
        logger.LogDebug("Fetching current highlight count from server");

        SettingsResponse response;
        try
        {
            response = await client.GetSettingsAsync(cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine($"Highlights per recap: [bold]{response.Count}[/]");
        return 0;
    }

    private async Task<int> SetCountAsync(Settings settings, CancellationToken cancellation)
    {
        if (!int.TryParse(settings.Value, out var count))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] [yellow]{settings.Value}[/] is not a valid number.");
            return 1;
        }

        if (count < 1 || count > 15)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Count must be between [green]1[/] and [green]15[/] (got [yellow]{count}[/]).");
            return 1;
        }

        logger.LogDebug("Setting highlight count to {Count}", count);

        var request = new UpdateSettingsRequest { Count = count };

        SettingsResponse response;
        try
        {
            response = await client.PatchSettingsAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Highlights per recap set to [bold]{response.Count}[/]");
        return 0;
    }

}
