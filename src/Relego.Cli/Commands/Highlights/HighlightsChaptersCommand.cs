using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;

namespace Relego.Cli.Commands.Highlights;

/// <summary>
/// Shows one book's chapter tree with the number of annotations each chapter holds.
/// Usage: relego highlights chapters --book &lt;id&gt;
/// </summary>
public sealed class HighlightsChaptersCommand(RelegoHttpClient client, ILogger<HighlightsChaptersCommand> logger)
    : ServerCommand<HighlightsChaptersCommand.Settings>
{
    private const int CountColumn = 44;

    private static readonly CultureInfo OutputCulture = CultureInfo.InvariantCulture;

    protected override ILogger Logger => logger;

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--book <ID>")]
        [Description("Book whose chapters to show.")]
        public int? Book { get; set; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        if (settings.Book is not { } bookId || bookId <= 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", Markup.Escape("--book <id> is required."));
            return 1;
        }

        logger.LogDebug("Fetching chapters for book {BookId}", bookId);

        TableOfContentsResponse toc;
        try
        {
            toc = await client.GetChaptersAsync(bookId, cancellation);
        }
        catch (HttpRequestException ex)
        {
            return HandleServerError(ex);
        }

        if (!toc.HasTableOfContents)
        {
            var title = await ResolveBookTitleAsync(bookId, cancellation);
            AnsiConsole.MarkupLine(
                "[grey]{0}[/]",
                Markup.Escape($"{title} has no chapters yet. Add .h1 markers to create them."));
            return 0;
        }

        var roots = toc.Chapters.Where(chapter => chapter.ParentId is null).ToList();
        var children = toc.Chapters
            .Where(chapter => chapter.ParentId is not null)
            .GroupBy(chapter => chapter.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var root in roots)
        {
            RenderSubtree(root, depth: 0, children);
        }

        // Annotations outside every chapter stay reachable, so they are listed last and only when
        // the book actually has any.
        if (toc.UnknownChapter.AnnotationCount > 0)
        {
            AnsiConsole.MarkupLine("{0}", FormatLine(string.Empty, string.Empty, toc.UnknownChapter.Title, toc.UnknownChapter.AnnotationCount));
        }

        return 0;
    }

    private static void RenderSubtree(ChapterDto chapter, int depth, Dictionary<int, List<ChapterDto>> children)
    {
        var indent = new string(' ', depth * 2);
        // Level-1 headings are flush left; every deeper level indents and is drawn as a child.
        var prefix = depth == 0 ? string.Empty : "└ ";

        AnsiConsole.MarkupLine("{0}", FormatLine(indent, prefix, chapter.Title, chapter.AnnotationCount));

        if (chapter.Id is { } parentId && children.TryGetValue(parentId, out var nested))
        {
            foreach (var child in nested)
            {
                RenderSubtree(child, depth + 1, children);
            }
        }
    }

    private static string FormatLine(string indent, string prefix, string title, int annotationCount)
    {
        var left = $"{indent}{prefix}{title}";
        var padding = new string(' ', Math.Max(2, CountColumn - left.Length));
        var count = annotationCount == 1 ? "1 annotation" : $"{annotationCount.ToString(OutputCulture)} annotations";

        return Markup.Escape(left) + padding + Markup.Escape(count);
    }

    /// <summary>
    /// The table of contents carries the book id but not its title, and the "no chapters" line has
    /// to name the book to be actionable. Any one of the book's annotations knows its title.
    /// </summary>
    private async Task<string> ResolveBookTitleAsync(int bookId, CancellationToken cancellation)
    {
        try
        {
            var page = await client.GetHighlightsAsync(1, 1, null, bookId, null, null, cancellation).ConfigureAwait(false);
            var title = page.Items.FirstOrDefault()?.BookTitle;
            return string.IsNullOrWhiteSpace(title) ? $"Book {bookId}" : title;
        }
        catch (HttpRequestException)
        {
            return $"Book {bookId}";
        }
    }
}
