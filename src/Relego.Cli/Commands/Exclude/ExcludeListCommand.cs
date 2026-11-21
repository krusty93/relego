using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;

namespace Relego.Cli.Commands.Exclude;

/// <summary>
/// Lists all current exclusions grouped by type.
/// Usage: relego exclude list
/// </summary>
public sealed class ExcludeListCommand(RelegoHttpClient client, ILogger<ExcludeListCommand> logger)
    : ServerCommand<ExcludeListCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        logger.LogDebug("Fetching exclusions from server");

        Relego.Core.Contracts.ExclusionsResponse response;
        try
        {
            response = await client.GetExclusionsAsync(cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        AnsiConsole.MarkupLine("[bold]Excluded Highlights[/]");
        if (response.Highlights.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]None[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("ID");
            table.AddColumn("Text");
            table.AddColumn("Book");
            foreach (var h in response.Highlights)
                table.AddRow(h.Id.ToString(), Markup.Escape(h.Text), Markup.Escape(h.BookTitle));
            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Excluded Books[/]");
        if (response.Books.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]None[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("ID");
            table.AddColumn("Title");
            table.AddColumn("Author");
            table.AddColumn("Highlights");
            foreach (var b in response.Books)
                table.AddRow(
                    b.Id.ToString(),
                    Markup.Escape(b.Title),
                    Markup.Escape(b.AuthorName ?? "-"),
                    b.HighlightCount.ToString());
            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Excluded Authors[/]");
        if (response.Authors.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]None[/]");
        }
        else
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("ID");
            table.AddColumn("Name");
            table.AddColumn("Books");
            foreach (var a in response.Authors)
                table.AddRow(a.Id.ToString(), Markup.Escape(a.Name ?? "-"), a.BookCount.ToString());
            AnsiConsole.Write(table);
        }

        return 0;
    }
}
