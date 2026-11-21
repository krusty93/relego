using System.Data;
using Dapper;
using Relego.Core.Annotations;
using Relego.Core.Contracts;

namespace Relego.Server.Services;

/// <summary>
/// The derived information of one book: the chapter hierarchy, the table of contents shaped for
/// the API, the stitched passages and each annotation's tags. All of it comes from the same
/// derivation, so the navigation and the annotation list cannot disagree.
/// </summary>
/// <param name="Tree">Derived chapter hierarchy of the book.</param>
/// <param name="TableOfContents">Table of contents as returned by <c>GET /books/{id}/chapters</c>.</param>
/// <param name="Stitches">Derived stitched passages of the book.</param>
/// <param name="Tags">Tags of each annotation, keyed by annotation id, orphan markers included.</param>
public sealed record BookAnnotationContext(
    ChapterTree Tree,
    TableOfContentsResponse TableOfContents,
    StitchIndex Stitches,
    IReadOnlyDictionary<int, IReadOnlyList<string>> Tags);

/// <summary>
/// The derived information of a page of annotations: chapter context, stitch membership and tags,
/// keyed by annotation id and derived from each annotation's whole book.
/// </summary>
/// <param name="Chapters">Chapter context of every annotation.</param>
/// <param name="Stitches">Stitched passages of the books the page touches.</param>
/// <param name="Tags">Tags of every annotation, orphan markers included.</param>
public sealed record AnnotationDerivations(
    IReadOnlyDictionary<int, AnnotationContext> Chapters,
    StitchIndex Stitches,
    IReadOnlyDictionary<int, IReadOnlyList<string>> Tags);

/// <summary>
/// The single place that turns stored rows into derived annotation context. Every surface reads
/// chapters, stitches and tags through this service, so no endpoint or client re-derives them.
/// </summary>
public sealed class AnnotationContextService(IDbConnection connection)
{
    /// <summary>Title reported for annotations that belong to no chapter.</summary>
    public const string UnknownChapterTitle = "Unknown chapter";

    /// <summary>
    /// Derives the chapter hierarchy, the table of contents, the stitched passages and the tags of
    /// one book.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="bookId">Book to derive.</param>
    /// <returns>The derived context, or <c>null</c> when the book does not exist for the user.</returns>
    public async Task<BookAnnotationContext?> GetBookContextAsync(int userId, int bookId)
    {
        var owned = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM books WHERE id = @BookId AND user_id = @UserId",
            new { BookId = bookId, UserId = userId }).ConfigureAwait(false);

        if (owned == 0)
            return null;

        var annotations = await QueryAnnotationsAsync(userId, [bookId]).ConfigureAwait(false);
        var tree = BuildTree(annotations);
        var stitches = BuildStitches(annotations);

        return new BookAnnotationContext(
            tree,
            ToTableOfContents(bookId, tree),
            stitches,
            BuildTags(annotations, stitches));
    }

    /// <summary>
    /// Derives the chapter context, the stitched passages and the tags of every annotation of the
    /// given books. Derivation runs per book, so a page of annotations still knows its whole book's
    /// structure.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="bookIds">Books whose rows are needed.</param>
    /// <returns>The derived lookups; empty when no book is given.</returns>
    public async Task<AnnotationDerivations> GetDerivationsAsync(
        int userId,
        IReadOnlyCollection<int> bookIds)
    {
        ArgumentNullException.ThrowIfNull(bookIds);

        var chapters = new Dictionary<int, AnnotationContext>();
        var tags = new Dictionary<int, IReadOnlyList<string>>();

        if (bookIds.Count == 0)
            return new AnnotationDerivations(chapters, BuildStitches([]), tags);

        var annotations = await QueryAnnotationsAsync(userId, bookIds).ConfigureAwait(false);

        foreach (var book in annotations.GroupBy(annotation => annotation.BookId))
        {
            var tree = BuildTree(book);

            foreach (var pair in tree.Contexts)
                chapters[pair.Key] = pair.Value;
        }

        var stitches = BuildStitches(annotations);

        return new AnnotationDerivations(chapters, stitches, BuildTags(annotations, stitches));
    }

    private static ChapterTree BuildTree(IEnumerable<AnnotationRow> annotations) =>
        ChapterTreeBuilder.Build(annotations.Select(annotation => new ChapterSourceAnnotation
        {
            Id = annotation.Id,
            Text = annotation.Text,
            Note = annotation.Note,
            HeadingLevel = annotation.HeadingLevel,
            Position = annotation.Position,
            AddedOn = annotation.AddedOn,
        }));

    private static StitchIndex BuildStitches(IEnumerable<AnnotationRow> annotations) =>
        StitchedPassageBuilder.Build(annotations.Select(annotation => new StitchSourceAnnotation
        {
            Id = annotation.Id,
            BookId = annotation.BookId,
            Text = annotation.Text,
            Note = annotation.Note,
            StitchLevel = annotation.StitchLevel,
            Tags = SplitTags(annotation.Tags),
            Position = annotation.Position,
            AddedOn = annotation.AddedOn,
            Weight = annotation.Weight,
            Excluded = annotation.Excluded,
            DeliveryCount = annotation.DeliveryCount,
        }));

    // An orphaned ".cN" marker is not persisted as a tag: only the whole book shows that no
    // preceding ".c1" exists, so the tag is added here, at read time (Story 6 AS-2).
    private static IReadOnlyDictionary<int, IReadOnlyList<string>> BuildTags(
        IEnumerable<AnnotationRow> annotations,
        StitchIndex stitches)
    {
        var tags = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var annotation in annotations)
        {
            var parsed = SplitTags(annotation.Tags);

            if (stitches.OrphanMarkerTags.TryGetValue(annotation.Id, out var marker)
                && !parsed.Any(tag => string.Equals(tag, marker, StringComparison.OrdinalIgnoreCase)))
            {
                parsed = [.. parsed, marker];
            }

            tags[annotation.Id] = parsed;
        }

        return tags;
    }

    // Tags are stored space-separated because a note declares them that way; the column is the
    // only persisted form, so the split must stay in step with the import writer.
    private static List<string> SplitTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : [.. raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private static TableOfContentsResponse ToTableOfContents(int bookId, ChapterTree tree) => new()
    {
        BookId = bookId,
        HasTableOfContents = tree.HasTableOfContents,
        // Deeper markers are shown as headings, but alone they are not a navigation (FR-026).
        Chapters = tree.HasTableOfContents ? [.. tree.Chapters.Select(ToChapter)] : [],
        UnknownChapter = new ChapterDto
        {
            Id = null,
            Title = UnknownChapterTitle,
            Level = 0,
            AnnotationCount = tree.UnknownChapterAnnotationCount,
        },
    };

    private static ChapterDto ToChapter(Chapter chapter) => new()
    {
        Id = chapter.Id,
        Title = chapter.Title,
        Level = chapter.Level,
        ParentId = chapter.ParentId,
        Note = chapter.Note,
        Position = chapter.Position,
        AddedOn = chapter.AddedOn,
        AnnotationCount = chapter.AnnotationCount,
    };

    private async Task<IReadOnlyList<AnnotationRow>> QueryAnnotationsAsync(
        int userId,
        IReadOnlyCollection<int> bookIds) =>
        [.. await connection.QueryAsync<AnnotationRow>(
            """
            SELECT
                h.book_id       AS BookId,
                h.id            AS Id,
                h.text          AS Text,
                h.note          AS Note,
                h.heading_level AS HeadingLevel,
                h.stitch_level  AS StitchLevel,
                h.tags          AS Tags,
                h.weight        AS Weight,
                h.excluded      AS Excluded,
                h.delivery_count AS DeliveryCount,
                h.source_position AS Position,
                h.added_on      AS AddedOn
            FROM highlights h
            WHERE h.user_id = @UserId
              AND h.book_id IN @BookIds
            ORDER BY h.id ASC
            """,
            new { UserId = userId, BookIds = bookIds }).ConfigureAwait(false)];

    private sealed class AnnotationRow
    {
        public int BookId { get; set; }
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int? HeadingLevel { get; set; }
        public int? StitchLevel { get; set; }
        public string? Tags { get; set; }
        public int Weight { get; set; }
        public bool Excluded { get; set; }
        public int DeliveryCount { get; set; }
        public string? Position { get; set; }
        public DateTimeOffset? AddedOn { get; set; }
    }
}
