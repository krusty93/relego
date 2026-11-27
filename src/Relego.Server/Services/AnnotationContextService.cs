using System.Data;
using Dapper;
using Relego.Core.Annotations;
using Relego.Core.Contracts;

namespace Relego.Server.Services;

/// <summary>
/// The derived chapter information of one book: the hierarchy itself plus the table of contents
/// shaped for the API. Both come from the same derivation, so the navigation and the annotation
/// list cannot disagree.
/// </summary>
/// <param name="Tree">Derived chapter hierarchy of the book.</param>
/// <param name="TableOfContents">Table of contents as returned by <c>GET /books/{id}/chapters</c>.</param>
public sealed record BookAnnotationContext(ChapterTree Tree, TableOfContentsResponse TableOfContents);

/// <summary>
/// The single place that turns stored rows into derived annotation context. Every surface reads
/// chapters through this service, so no endpoint or client re-derives them.
/// </summary>
public sealed class AnnotationContextService(IDbConnection connection)
{
    /// <summary>Title reported for annotations that belong to no chapter.</summary>
    public const string UnknownChapterTitle = "Unknown chapter";

    /// <summary>
    /// Derives the chapter hierarchy and the table of contents of one book.
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

        return new BookAnnotationContext(tree, ToTableOfContents(bookId, tree));
    }

    /// <summary>
    /// Derives the chapter context of every annotation of the given books. Derivation runs per
    /// book, so a page of annotations still knows its whole book's structure.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="bookIds">Books whose rows are needed.</param>
    /// <returns>Chapter context keyed by annotation id; empty when no book is given.</returns>
    public async Task<IReadOnlyDictionary<int, AnnotationContext>> GetContextsAsync(
        int userId,
        IReadOnlyCollection<int> bookIds)
    {
        ArgumentNullException.ThrowIfNull(bookIds);

        var contexts = new Dictionary<int, AnnotationContext>();

        if (bookIds.Count == 0)
            return contexts;

        var annotations = await QueryAnnotationsAsync(userId, bookIds).ConfigureAwait(false);

        foreach (var book in annotations.GroupBy(annotation => annotation.BookId))
        {
            var tree = BuildTree(book);

            foreach (var pair in tree.Contexts)
                contexts[pair.Key] = pair.Value;
        }

        return contexts;
    }

    private static ChapterTree BuildTree(IEnumerable<ChapterRow> annotations) =>
        ChapterTreeBuilder.Build(annotations.Select(annotation => new ChapterSourceAnnotation
        {
            Id = annotation.Id,
            Text = annotation.Text,
            Note = annotation.Note,
            HeadingLevel = annotation.HeadingLevel,
            Position = annotation.Position,
            AddedOn = annotation.AddedOn,
        }));

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

    private async Task<IReadOnlyList<ChapterRow>> QueryAnnotationsAsync(
        int userId,
        IReadOnlyCollection<int> bookIds) =>
        [.. await connection.QueryAsync<ChapterRow>(
            """
            SELECT
                h.book_id       AS BookId,
                h.id            AS Id,
                h.text          AS Text,
                h.note          AS Note,
                h.heading_level AS HeadingLevel,
                h.source_position AS Position,
                h.added_on      AS AddedOn
            FROM highlights h
            WHERE h.user_id = @UserId
              AND h.book_id IN @BookIds
            ORDER BY h.id ASC
            """,
            new { UserId = userId, BookIds = bookIds }).ConfigureAwait(false)];

    private sealed class ChapterRow
    {
        public int BookId { get; set; }
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int? HeadingLevel { get; set; }
        public string? Position { get; set; }
        public DateTimeOffset? AddedOn { get; set; }
    }
}
