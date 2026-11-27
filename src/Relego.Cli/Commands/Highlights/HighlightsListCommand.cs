using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Highlights;

/// <summary>
/// Lists annotations with their note, tags, chapter path, position and reading date.
/// Usage: relego highlights list [--book &lt;id&gt;] [--chapter &lt;id|unknown&gt;] [--tag &lt;name&gt;]
/// [--search &lt;text&gt;] [--page &lt;n&gt;] [--page-size &lt;n&gt;]
/// </summary>
public sealed class HighlightsListCommand(RelegoHttpClient client, ILogger<HighlightsListCommand> logger)
    : ServerCommand<HighlightsListCommand.Settings>
{
    private const string UnknownAuthor = "Unknown author";
    private const string Indent = "      ";

    private static readonly CultureInfo OutputCulture = CultureInfo.InvariantCulture;

    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--book <ID>")]
        [Description("Restrict the list to one book.")]
        public int? Book { get; set; }

        [CommandOption("--chapter <ID>")]
        [Description("Restrict the list to one chapter of --book: a chapter id, or \"unknown\".")]
        public string? Chapter { get; set; }

        [CommandOption("--tag <NAME>")]
        [Description("Restrict the list to annotations carrying the tag, matched without regard to case.")]
        public string? Tag { get; set; }

        [CommandOption("--search <TEXT>")]
        [Description("Free text over the passage, note, book title and author name.")]
        public string? Search { get; set; }

        [CommandOption("--page <N>")]
        [Description("Page number, starting at 1.")]
        public int Page { get; set; } = 1;

        [CommandOption("--page-size <N>")]
        [Description("Annotations per page, from 1 to 200.")]
        public int PageSize { get; set; } = 50;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        // A chapter id only means something inside a book, so the pairing is required rather
        // than silently ignored the way the server would do.
        if (!string.IsNullOrWhiteSpace(settings.Chapter) && settings.Book is null)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape("--chapter requires --book <id>.")}");
            return 1;
        }

        if (settings.Page < 1 || settings.PageSize is < 1 or > 200)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --page must be at least 1 and --page-size between 1 and 200.");
            return 1;
        }

        logger.LogDebug("Fetching annotations from server");

        HighlightsResponse response;
        try
        {
            response = await client.GetHighlightsAsync(
                settings.Page,
                settings.PageSize,
                settings.Search,
                settings.Book,
                settings.Chapter,
                settings.Tag,
                cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        if (response.Items.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No annotations found.[/]");
            return 0;
        }

        var fragmentCounts = new Dictionary<int, int>();
        foreach (var item in response.Items.Where(item => item.StitchGroupId is not null))
        {
            var groupId = item.StitchGroupId!.Value;
            fragmentCounts[groupId] = fragmentCounts.GetValueOrDefault(groupId) + 1;
        }

        var first = true;
        foreach (var item in response.Items)
        {
            // The group's later fragments are already part of the joined passage printed for its
            // first fragment, so printing them again would show the same words twice.
            if (item.StitchGroupId is not null && item.StitchedText is null)
            {
                continue;
            }

            if (!first)
            {
                AnsiConsole.WriteLine();
            }

            first = false;

            Render(item, item.StitchGroupId is { } groupId ? fragmentCounts.GetValueOrDefault(groupId) : 0);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]{0}[/]", Markup.Escape(FormatPageFooter(response)));

        return 0;
    }

    private static void Render(HighlightItemDto item, int fragmentCount)
    {
        AnsiConsole.MarkupLine(
            "{0}  {1} — {2}",
            item.Id.ToString(OutputCulture),
            Markup.Escape(item.BookTitle),
            Markup.Escape(item.AuthorName ?? UnknownAuthor));

        var passage = item.StitchedText ?? item.Text;

        // A note-only annotation still gets its passage line: the block must never disappear.
        AnsiConsole.MarkupLine(passage.Length == 0 ? $"{Indent}❝" : $"{Indent}❝ {Markup.Escape(passage)}");

        if (fragmentCount > 1)
        {
            AnsiConsole.MarkupLine($"{Indent}[grey](stitched from {fragmentCount.ToString(OutputCulture)} highlights)[/]");
        }

        if (item.HeadingLevel is { } level)
        {
            AnsiConsole.MarkupLine($"{Indent}⬒ heading (level {level.ToString(OutputCulture)})");
        }

        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            AnsiConsole.MarkupLine($"{Indent}✎ {Markup.Escape(item.Note)}");
        }

        if (item.Tags.Count > 0)
        {
            AnsiConsole.MarkupLine($"{Indent}{string.Join(" ", item.Tags.Select(tag => $"#{Markup.Escape(tag)}"))}");
        }

        var meta = new List<string>();

        // A heading is its own chapter, so its path would only repeat the passage above it.
        if (item.ChapterPath.Count > 0 && item.HeadingLevel is null)
        {
            meta.Add($"Chapter: {string.Join(" › ", item.ChapterPath.Select(Markup.Escape))}");
        }

        if (!string.IsNullOrWhiteSpace(item.Position))
        {
            meta.Add(Markup.Escape(item.Position));
        }

        if (item.AddedOn is { } addedOn)
        {
            meta.Add(addedOn.ToLocalTime().ToString("yyyy-MM-dd", OutputCulture));
        }

        if (meta.Count > 0)
        {
            AnsiConsole.MarkupLine($"{Indent}[grey]{string.Join("    ", meta)}[/]");
        }
    }

    private static string FormatPageFooter(HighlightsResponse response)
    {
        var pages = Math.Max(1, (response.Total + response.PageSize - 1) / response.PageSize);
        var count = response.Total == 1
            ? "1 annotation"
            : $"{response.Total.ToString(OutputCulture)} annotations";

        return $"{count} · page {response.Page.ToString(OutputCulture)} of {pages.ToString(OutputCulture)}";
    }
}
