using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config;

/// <summary>
/// Displays all current server settings in a unified table.
/// Usage: relego config show
/// </summary>
public sealed class ConfigShowCommand(RelegoHttpClient client, ILogger<ConfigShowCommand> logger)
    : ServerCommand<ConfigShowCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        logger.LogDebug("Fetching all settings from server");

        SettingsResponse response;
        try
        {
            response = await client.GetSettingsAsync(cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");

        table.AddRow("Schedule", response.Schedule);
        if (response.DeliveryDay is not null)
            table.AddRow("Delivery Day", response.DeliveryDay);
        table.AddRow("Delivery Time", response.DeliveryTime);
        table.AddRow("Count", response.Count.ToString());
        table.AddRow("Kindle Email", response.KindleEmail);
        table.AddRow("Also Email Recap to (opt.)", response.DeliveryEmail ?? "[grey](not set — optional)[/]");
        table.AddRow("Timezone", response.Timezone);

        AnsiConsole.Write(table);
        return 0;
    }
}
