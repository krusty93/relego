using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config;

/// <summary>
/// Sets the Kindle delivery email address on the server.
/// Usage: relego config kindle-email &lt;address&gt;
/// </summary>
public sealed partial class ConfigKindleEmailCommand(RelegoHttpClient client, ILogger<ConfigKindleEmailCommand> logger)
    : ServerCommand<ConfigKindleEmailCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<address>")]
        [Description("Send-to-Kindle email address (e.g. yourname_XXXX@kindle.com).")]
        public string Address { get; set; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var address = settings.Address.Trim();

        if (!EmailRegex().IsMatch(address))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] [yellow]{Markup.Escape(address)}[/] is not a valid email address.");
            return 1;
        }

        logger.LogDebug("Setting Kindle email to {Address}", address);

        var request = new UpdateSettingsRequest { KindleEmail = address };

        SettingsResponse response;
        try
        {
            response = await client.PatchSettingsAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Kindle email set to [bold]{Markup.Escape(response.KindleEmail ?? address)}[/].");
        return 0;
    }

    [GeneratedRegex(
        @"^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
