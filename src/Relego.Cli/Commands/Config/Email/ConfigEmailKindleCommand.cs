using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config.Email;

/// <summary>
/// Sets the Send-to-Kindle email address.
/// Usage: relego config email kindle &lt;address&gt;
/// Pass an empty string to clear the address.
/// </summary>
public sealed partial class ConfigEmailKindleCommand(RelegoHttpClient client, ILogger<ConfigEmailKindleCommand> logger)
    : ServerCommand<ConfigEmailKindleCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<address>")]
        [Description("Send-to-Kindle email address (e.g. yourname_XXXX@kindle.com). Pass \"\" to clear.")]
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

        logger.LogDebug("Setting Send-to-Kindle email to {Address}", address);

        SettingsResponse response;
        try
        {
            var current = await client.GetSettingsAsync(cancellation);
            var request = new UpdateSettingsRequest
            {
                KindleEmail = address,
                DeliveryEmail = current.DeliveryEmail,
            };
            response = await client.PatchSettingsAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        if (address.Length == 0)
        {
            AnsiConsole.MarkupLine("[green]✓[/] Send to Kindle address cleared.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Send to Kindle set to [bold]{Markup.Escape(response.KindleEmail ?? address)}[/].");
        }
        return 0;
    }

    [GeneratedRegex(
        @"^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
