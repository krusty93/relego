using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Config;

/// <summary>
/// Configures the recap schedule (cadence and delivery time) on the server.
/// Usage: relego config schedule daily|weekly HH:mm
///        relego config schedule show
/// </summary>
public sealed partial class ConfigScheduleCommand(RelegoHttpClient client, ILogger<ConfigScheduleCommand> logger)
    : ServerCommand<ConfigScheduleCommand.Settings>
{
    protected override ILogger Logger => logger;

    private static readonly string[] ValidCadences = ["daily", "weekly"];

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<cadence>")]
        [Description("Recap cadence: daily, weekly, or 'show' to display current schedule.")]
        public string Cadence { get; set; } = string.Empty;

        [CommandArgument(1, "[time]")]
        [Description("Delivery time in HH:mm format (e.g. 08:00).")]
        public string? Time { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        if (settings.Cadence.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            return await ShowScheduleAsync(cancellation);
        }

        return await SetScheduleAsync(settings, cancellation);
    }

    private async Task<int> ShowScheduleAsync(CancellationToken cancellation)
    {
        logger.LogDebug("Fetching current schedule from server");

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
        table.AddRow("Timezone", response.Timezone);

        AnsiConsole.Write(table);
        return 0;
    }

    private async Task<int> SetScheduleAsync(Settings settings, CancellationToken cancellation)
    {
        var cadence = settings.Cadence.ToLowerInvariant();

        if (!ValidCadences.Contains(cadence))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Invalid cadence [yellow]{settings.Cadence}[/]. Use [green]daily[/] or [green]weekly[/].");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Time))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Delivery time is required. Use format [green]HH:mm[/] (e.g. 08:00).");
            return 1;
        }

        if (!TimeFormatRegex().IsMatch(settings.Time))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Invalid time format [yellow]{settings.Time}[/]. Use [green]HH:mm[/] (00:00–23:59).");
            return 1;
        }

        var timezone = TimeZoneInfo.Local.Id;
        logger.LogDebug("Setting schedule: {Cadence} at {Time}, timezone {Timezone}", cadence, settings.Time, timezone);

        var request = new UpdateSettingsRequest
        {
            Schedule = cadence,
            DeliveryTime = settings.Time,
            Timezone = timezone,
        };

        SettingsResponse response;
        try
        {
            response = await client.PatchSettingsAsync(request, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Schedule set to [bold]{response.Schedule}[/] at [bold]{response.DeliveryTime}[/] ({response.Timezone})");
        return 0;
    }

    [GeneratedRegex(@"^(?:[01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimeFormatRegex();
}
