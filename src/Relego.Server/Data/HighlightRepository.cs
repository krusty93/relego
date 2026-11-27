using System.Data;
using Dapper;
using Relego.Core.Contracts;
using Relego.Server.Services;

namespace Relego.Server.Data;

public sealed class HighlightRepository(IDbConnection connection, AnnotationContextService annotationContext)
{
    public async Task<HighlightsResponse> GetHighlightsAsync(
        int userId,
        int page,
        int pageSize,
        string? q,
        int? bookId = null,
        int? chapterId = null,
        bool unknownChapter = false)
    {
        // A chapter only exists inside a book; without one the filter has nothing to resolve.
        if (bookId is not null && (chapterId is not null || unknownChapter))
            return await GetChapterSectionAsync(userId, page, pageSize, q, bookId.Value, chapterId, unknownChapter);

        var hasFilter = !string.IsNullOrWhiteSpace(q);
        var filter = hasFilter ? $"%{q}%" : null;
        var bookCondition = bookId is null ? string.Empty : " AND h.book_id = @BookId";

        var countSql = hasFilter
            ? $"""
               SELECT COUNT(*)
               FROM highlights h
               INNER JOIN books b ON b.id = h.book_id
               INNER JOIN authors a ON a.id = b.author_id
               WHERE h.user_id = @UserId
                 AND (h.text LIKE @Filter OR h.note LIKE @Filter OR b.title LIKE @Filter OR a.name LIKE @Filter){bookCondition}
               """
            : $"SELECT COUNT(*) FROM highlights h WHERE h.user_id = @UserId{bookCondition}";

        var itemSql = hasFilter
            ? $"""
               SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName, h.note AS Note, h.heading_level AS HeadingLevel, h.source_position AS Position, h.added_on AS AddedOn
               FROM highlights h
               INNER JOIN books b ON b.id = h.book_id
               INNER JOIN authors a ON a.id = b.author_id
               WHERE h.user_id = @UserId
                 AND (h.text LIKE @Filter OR h.note LIKE @Filter OR b.title LIKE @Filter OR a.name LIKE @Filter){bookCondition}
               ORDER BY h.id ASC
               LIMIT @PageSize OFFSET @Offset
               """
            : $"""
               SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName, h.note AS Note, h.heading_level AS HeadingLevel, h.source_position AS Position, h.added_on AS AddedOn
               FROM highlights h
               INNER JOIN books b ON b.id = h.book_id
               INNER JOIN authors a ON a.id = b.author_id
               WHERE h.user_id = @UserId{bookCondition}
               ORDER BY h.id ASC
               LIMIT @PageSize OFFSET @Offset
               """;

        var param = new
        {
            UserId = userId,
            Filter = filter,
            BookId = bookId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        };

        var total = await connection.ExecuteScalarAsync<int>(countSql, param);
        var items = (await connection.QueryAsync<HighlightItemDto>(itemSql, param)).AsList();

        await AttachChapterContextAsync(userId, items);

        return new HighlightsResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    // A chapter filter needs the whole book to know which chapter every annotation sits in, so it
    // loads the book's rows, derives once, and only then matches the free-text filter and pages.
    private async Task<HighlightsResponse> GetChapterSectionAsync(
        int userId,
        int page,
        int pageSize,
        string? q,
        int bookId,
        int? chapterId,
        bool unknownChapter)
    {
        var empty = new HighlightsResponse { Total = 0, Page = page, PageSize = pageSize, Items = [] };
        var bookContext = await annotationContext.GetBookContextAsync(userId, bookId);

        if (bookContext is null)
            return empty;

        var rows = (await connection.QueryAsync<HighlightItemDto>(
            """
            SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName, h.note AS Note, h.heading_level AS HeadingLevel, h.source_position AS Position, h.added_on AS AddedOn
            FROM highlights h
            INNER JOIN books b ON b.id = h.book_id
            INNER JOIN authors a ON a.id = b.author_id
            WHERE h.user_id = @UserId
              AND h.book_id = @BookId
            ORDER BY h.id ASC
            """,
            new { UserId = userId, BookId = bookId })).AsList();

        Func<HighlightItemDto, bool> matchesQuery = string.IsNullOrWhiteSpace(q)
            ? _ => true
            : item => MatchesQuery(item, q);

        IReadOnlySet<int>? section = null;

        if (chapterId is not null)
        {
            section = bookContext.Tree.WithDescendants(chapterId.Value);

            if (section.Count == 0)
                return empty;
        }

        var selected = new List<HighlightItemDto>();

        foreach (var item in rows)
        {
            if (!matchesQuery(item))
                continue;

            bookContext.Tree.Contexts.TryGetValue(item.Id, out var context);
            var itemChapterId = context?.ChapterId;

            var inSelection = section is null
                ? unknownChapter ? itemChapterId is null : itemChapterId is not null
                : itemChapterId is int value && section.Contains(value);

            if (!inSelection)
                continue;

            if (context is not null)
            {
                item.ChapterId = context.ChapterId;
                item.ChapterPath = [.. context.ChapterPath];
            }

            selected.Add(item);
        }

        return new HighlightsResponse
        {
            Total = selected.Count,
            Page = page,
            PageSize = pageSize,
            Items = [.. selected.Skip((page - 1) * pageSize).Take(pageSize)],
        };
    }

    // The free-text filter of the chapter path runs in memory because the rows are already loaded
    // to derive chapters; it mirrors the SQL LIKE clauses of the unfiltered path.
    private static bool MatchesQuery(HighlightItemDto item, string q) =>
        item.Text.Contains(q, StringComparison.OrdinalIgnoreCase)
        || (item.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
        || item.BookTitle.Contains(q, StringComparison.OrdinalIgnoreCase)
        || (item.AuthorName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

    private async Task AttachChapterContextAsync(int userId, IReadOnlyCollection<HighlightItemDto> items)
    {
        if (items.Count == 0)
            return;

        var bookIds = items.Select(item => item.BookId).Distinct().ToList();
        var contexts = await annotationContext.GetContextsAsync(userId, bookIds);

        foreach (var item in items)
        {
            if (contexts.TryGetValue(item.Id, out var context))
            {
                item.ChapterId = context.ChapterId;
                item.ChapterPath = [.. context.ChapterPath];
            }
            else
            {
                item.ChapterPath = [];
            }
        }
    }

    /// <summary>
    /// Deletes a highlight and the provenance row a cloud sync attached to it. Provenance
    /// references the highlight without a cascade, so deleting the parent first would fail the
    /// foreign key and surface as a 500.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="highlightId">Highlight to delete.</param>
    /// <returns><c>true</c> when the highlight existed and was deleted.</returns>
    public async Task<bool> DeleteHighlightAsync(int userId, int highlightId)
    {
        var param = new { HighlightId = highlightId, UserId = userId };
        using var transaction = connection.BeginTransaction();

        await connection.ExecuteAsync(
            """
            DELETE FROM highlight_provenance
            WHERE highlight_id IN (SELECT id FROM highlights WHERE id = @HighlightId AND user_id = @UserId)
            """,
            param,
            transaction);

        var affectedRows = await connection.ExecuteAsync(
            "DELETE FROM highlights WHERE id = @HighlightId AND user_id = @UserId",
            param,
            transaction);

        if (affectedRows > 0)
            transaction.Commit();
        else
            transaction.Rollback();

        return affectedRows > 0;
    }
}
