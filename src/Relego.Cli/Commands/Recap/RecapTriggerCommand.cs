using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;

namespace Relego.Cli.Commands.Recap;

/// <summary>
/// Triggers a recap immediately on the server.
/// Usage: relego recap trigger
/// </summary>
public sealed class RecapTriggerCommand(RelegoHttpClient client, ILogger<RecapTriggerCommand> logger)
    : ServerCommand<RecapTriggerCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        logger.LogDebug("Triggering immediate recap");

        Core.Contracts.SettingsResponse currentSettings;
        try
        {
            currentSettings = await client.GetSettingsAsync(cancellation);
        }
        catch (HttpRequestException)
        {
            // If we can't reach the server to pre-check, fall through — the trigger call will surface the error.
            currentSettings = null!;
        }

        if (currentSettings is not null
            && string.IsNullOrWhiteSpace(currentSettings.KindleEmail)
            && string.IsNullOrWhiteSpace(currentSettings.DeliveryEmail))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No delivery destination configured.");
            AnsiConsole.MarkupLine("[grey]Set a Kindle or inbox email first: [yellow]relego config email kindle <addr>[/] or [yellow]relego config email inbox <addr>[/][/]");
            return 1;
        }

        Core.Contracts.RecapTriggerResponse response;
        try
        {
            response = await client.TriggerRecapAsync(cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine($"[green]\u2713[/] Recap triggered. Scheduled for: [yellow]{response.ScheduledFor.ToLocalTime():yyyy-MM-dd HH:mm zzz}[/]");
        return 0;
    }
}
