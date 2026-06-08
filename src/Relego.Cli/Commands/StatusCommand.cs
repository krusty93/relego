using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;

namespace Relego.Cli.Commands;

/// <summary>
/// Displays server health and aggregate state.
/// Usage: relego status
/// </summary>
public sealed class StatusCommand(RelegoHttpClient client, ILogger<StatusCommand> logger)
    : ServerCommand<StatusCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        logger.LogDebug("Fetching server status");

        Relego.Core.Contracts.StatusResponse response;
        try
        {
            response = await client.GetStatusAsync(cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Total Highlights", response.TotalHighlights.ToString());
        table.AddRow("Total Books", response.TotalBooks.ToString());
        table.AddRow("Total Authors", response.TotalAuthors.ToString());
        table.AddRow("Excluded Highlights", response.ExcludedHighlights.ToString());
        table.AddRow("Excluded Books", response.ExcludedBooks.ToString());
        table.AddRow("Excluded Authors", response.ExcludedAuthors.ToString());
        table.AddRow("Kindle Email", FormatKindleEmail(response.KindleEmailConfigured));
        table.AddRow("Delivery Email", FormatDeliveryEmail(response.DeliveryEmailConfigured));
        table.AddRow("Next Recap", FormatTimestamp(response.NextRecap) ?? "[grey]Not scheduled[/]");
        table.AddRow("Last Recap Status", FormatLastStatus(response.LastRecapStatus));

        if (response.LastRecapStatus == "failed" && response.LastRecapError is not null)
            table.AddRow("Last Recap Error", Markup.Escape(response.LastRecapError));

        AnsiConsole.Write(table);
        return 0;
    }

    private static string? FormatTimestamp(string? utcIso)
    {
        if (string.IsNullOrWhiteSpace(utcIso))
            return null;

        if (!DateTimeOffset.TryParse(utcIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return utcIso;

        return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz");
    }

    private static string FormatKindleEmail(bool configured) =>
        configured
            ? "[green]\u2713 Configured[/]"
            : "[yellow]\u26a0 Not configured \u2014 run: relego config kindle-email <address>[/]";

    private static string FormatDeliveryEmail(bool configured) =>
        configured
            ? "[green]\u2713 Configured[/]"
            : "[grey]Not configured[/]";

    private static string FormatLastStatus(string? status) => status switch
    {
        "delivered" => "[green]delivered[/]",
        "failed" => "[red]failed[/]",
        null => "[grey]none[/]",
        _ => Markup.Escape(status),
    };
}
