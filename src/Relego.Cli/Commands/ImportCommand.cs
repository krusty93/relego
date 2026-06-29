using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;
using Relego.Cli.Import;

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

        // Both devices connected → render a per-source summary (T044).
        if (outcome.SourceOutcomes.Count > 1)
        {
            return HandleMultiSource(outcome);
        }

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
        DisplaySummary(outcome);
        return 0;
    }

    private static void DisplaySummary(ClippingsImportOutcome outcome)
    {
        var parseResult = outcome.ParseResult!;
        var response = outcome.Response!;
        var totalHighlights = parseResult.Books.Sum(book => book.Highlights.Count);

        var rows = new List<IRenderable>();
        if (outcome.Source is not null)
        {
            rows.Add(new Markup($"[green]✓[/] Detected [bold]{Markup.Escape(outcome.Source.DisplayName)}[/] source"));
        }

        rows.Add(new Markup($"[green]✓[/] Parsed [bold]{totalHighlights}[/] highlights from [bold]{parseResult.Books.Count}[/] books"));
        rows.Add(new Markup($"[green]✓[/] [bold]{response.NewHighlights}[/] new highlights imported ([grey]{response.DuplicateHighlights} duplicates skipped[/])"));
        rows.Add(new Markup($"[green]✓[/] [bold]{response.NewBooks}[/] new books, [bold]{response.NewAuthors}[/] new authors"));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Rows(rows))
            .Header("[green]Import Complete[/]")
            .Border(BoxBorder.Rounded));
    }

    private static int HandleMultiSource(ClippingsImportOutcome outcome)
    {
        var rows = new List<IRenderable>();
        foreach (var source in outcome.SourceOutcomes)
        {
            var name = Markup.Escape(source.Source?.DisplayName ?? "Source");
            switch (source.Status)
            {
                case ClippingsImportStatus.Succeeded:
                    rows.Add(new Markup(
                        $"[green]✓[/] [bold]{name}[/]: {source.TotalHighlightsParsed} parsed, " +
                        $"[bold]{source.Response!.NewHighlights}[/] new ([grey]{source.Response.DuplicateHighlights} duplicates skipped[/])"));
                    break;

                case ClippingsImportStatus.NoHighlightsFound:
                    rows.Add(new Markup($"[yellow]•[/] [bold]{name}[/]: no highlights found"));
                    break;

                default:
                    rows.Add(new Markup($"[red]✗[/] [bold]{name}[/]: {Markup.Escape(source.Message ?? "import failed")}"));
                    break;
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Rows(rows))
            .Header("[green]Import Complete[/]")
            .Border(BoxBorder.Rounded));

        // Per-source failure isolation: succeed if at least one source imported.
        return outcome.SourceOutcomes.Any(s => s.IsSuccessful) ? 0 : 1;
    }
}
