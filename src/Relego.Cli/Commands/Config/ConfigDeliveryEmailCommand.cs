using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config;

/// <summary>
/// Sets the optional secondary email address for HTML recap delivery on the server.
/// Usage: relego config delivery-email &lt;address&gt;
/// </summary>
public sealed partial class ConfigDeliveryEmailCommand(RelegoHttpClient client, ILogger<ConfigDeliveryEmailCommand> logger)
    : ServerCommand<ConfigDeliveryEmailCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<address>")]
        [Description("Optional email address to also receive the HTML recap (in addition to Kindle).")]
        public string Address { get; set; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var address = settings.Address.Trim();

        if (address.Length > 0 && !EmailRegex().IsMatch(address))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] [yellow]{Markup.Escape(address)}[/] is not a valid email address.");
            return 1;
        }

        logger.LogDebug("Setting delivery email to {Address}", address);

        var request = new UpdateSettingsRequest { DeliveryEmail = address };

        SettingsResponse response;
        try
        {
            response = await client.PatchSettingsAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        if (address.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Also Email Recap to address cleared.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Also Email Recap to [bold]{Markup.Escape(response.DeliveryEmail ?? address)}[/] (optional, in addition to Kindle).");
        }

        return 0;
    }

    [GeneratedRegex(
        @"^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
