using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Parsing;
using Relego.Cli.Import;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands;

/// <summary>
/// Parses a Kindle clippings file and imports highlights to the server.
/// </summary>
public sealed class ImportCommand(ClippingsImportWorkflow workflow, ILogger<ImportCommand> logger) : ServerCommand<ImportCommand.Settings>
{
    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to My Clippings.txt. Auto-detected if omitted.")]
        public string? Path { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var outcome = await workflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = settings.Path,
            ResolvePathAsync = settings.Path is null ? ResolvePathAsync : null
        }, cancellation).ConfigureAwait(false);

        return outcome.Status switch
        {
            ClippingsImportStatus.Cancelled => HandleCancelled(),
            ClippingsImportStatus.FileNotFound => HandleMissingFile(outcome),
            ClippingsImportStatus.ParseFailed => HandleParseFailure(outcome),
            ClippingsImportStatus.NoHighlightsFound => HandleNoHighlights(),
            ClippingsImportStatus.ServerError => HandleConnectivityFailure(outcome),
            ClippingsImportStatus.Succeeded => HandleSuccess(outcome),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome.Status), outcome.Status, null)
        };
    }

    private static ValueTask<string?> ResolvePathAsync(ClippingsImportPathPromptRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(!string.IsNullOrWhiteSpace(request.DetectedPath) ? request.DetectedPath : PromptForPath());
    }

    private static string? PromptForPath()
    {
        AnsiConsole.MarkupLine("[yellow]Kindle not found at default paths.[/]");
        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("[grey]Enter the path to My Clippings.txt, or press Enter to cancel:[/]")
                .AllowEmpty());

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static int HandleCancelled()
    {
        AnsiConsole.MarkupLine("[yellow]Import cancelled.[/]");
        return 1;
    }

    private static int HandleMissingFile(ClippingsImportOutcome outcome)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] File not found: [yellow]{outcome.FilePath}[/]");
        return 1;
    }

    private static int HandleParseFailure(ClippingsImportOutcome outcome)
    {
        AnsiConsole.MarkupLine($"[red]Error parsing clippings file:[/] {outcome.Message}");
        return 1;
    }

    private static int HandleNoHighlights()
    {
        AnsiConsole.MarkupLine("[yellow]No highlights found in the clippings file.[/]");
        return 0;
    }

    private int HandleConnectivityFailure(ClippingsImportOutcome outcome)
    {
        return HandleServerError(outcome.Error as HttpRequestException ?? new HttpRequestException(outcome.Message));
    }

    private static int HandleSuccess(ClippingsImportOutcome outcome)
    {
        DisplaySummary(outcome.ParseResult!, outcome.Response!);
        return 0;
    }

    private static void DisplaySummary(ParseResult parseResult, SyncResponse response)
    {
        var totalHighlights = parseResult.Books.Sum(book => book.Highlights.Count);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            new Rows(
                new Markup($"[green]✓[/] Parsed [bold]{totalHighlights}[/] highlights from [bold]{parseResult.Books.Count}[/] books"),
                new Markup($"[green]✓[/] [bold]{response.NewHighlights}[/] new highlights imported ([grey]{response.DuplicateHighlights} duplicates skipped[/])"),
                new Markup($"[green]✓[/] [bold]{response.NewBooks}[/] new books, [bold]{response.NewAuthors}[/] new authors")
            ))
            .Header("[green]Import Complete[/]")
            .Border(BoxBorder.Rounded));
    }
}
