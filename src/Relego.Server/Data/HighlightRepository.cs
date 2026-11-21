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
        bool unknownChapter = false,
        string? tag = null)
    {
        // A chapter only exists inside a book; without one the filter has nothing to resolve.
        if (bookId is not null && (chapterId is not null || unknownChapter))
            return await GetChapterSectionAsync(userId, page, pageSize, q, bookId.Value, chapterId, unknownChapter, tag);

        var hasFilter = !string.IsNullOrWhiteSpace(q);
        var filter = hasFilter ? $"%{q}%" : null;
        var hasTag = !string.IsNullOrWhiteSpace(tag);

        var conditions = new List<string> { "h.user_id = @UserId" };

        if (bookId is not null)
            conditions.Add("h.book_id = @BookId");

        // FR-051: a tag name is searchable like any other word, without changing what the other
        // clauses match.
        if (hasFilter)
            conditions.Add("(h.text LIKE @Filter OR h.note LIKE @Filter OR b.title LIKE @Filter OR a.name LIKE @Filter OR h.tags LIKE @Filter)");

        // Tags are stored space-separated, so surrounding the column with spaces makes the needle
        // an exact token: "prob" must not match "probably".
        if (hasTag)
            conditions.Add("instr(' ' || lower(coalesce(h.tags, '')) || ' ', @TagNeedle) > 0");

        var where = string.Join("\n  AND ", conditions);

        var countSql = $"""
            SELECT COUNT(*)
            FROM highlights h
            INNER JOIN books b ON b.id = h.book_id
            INNER JOIN authors a ON a.id = b.author_id
            WHERE {where}
            """;

        var itemSql = $"""
            SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName, h.note AS Note, h.heading_level AS HeadingLevel, h.source_position AS Position, h.added_on AS AddedOn
            FROM highlights h
            INNER JOIN books b ON b.id = h.book_id
            INNER JOIN authors a ON a.id = b.author_id
            WHERE {where}
            ORDER BY h.id ASC
            LIMIT @PageSize OFFSET @Offset
            """;

        var param = new
        {
            UserId = userId,
            Filter = filter,
            TagNeedle = hasTag ? $" {tag!.Trim().ToLowerInvariant()} " : null,
            BookId = bookId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize,
        };

        var total = await connection.ExecuteScalarAsync<int>(countSql, param);
        var items = (await connection.QueryAsync<HighlightItemDto>(itemSql, param)).AsList();

        await AttachDerivedContextAsync(userId, items);

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
        bool unknownChapter,
        string? tag)
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

        ApplyDerivations(bookContext, rows);

        Func<HighlightItemDto, bool> matchesQuery = string.IsNullOrWhiteSpace(q)
            ? _ => true
            : item => MatchesQuery(item, q);

        Func<HighlightItemDto, bool> matchesTag = string.IsNullOrWhiteSpace(tag)
            ? _ => true
            : item => MatchesTag(item.Tags, tag);

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
            if (!matchesQuery(item) || !matchesTag(item))
                continue;

            bookContext.Tree.Contexts.TryGetValue(item.Id, out var context);
            var itemChapterId = context?.ChapterId;

            var inSelection = section is null
                ? unknownChapter ? itemChapterId is null : itemChapterId is not null
                : itemChapterId is int value && section.Contains(value);

            if (!inSelection)
                continue;

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
        || (item.AuthorName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
        || item.Tags.Any(candidate => candidate.Contains(q, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesTag(IReadOnlyCollection<string> tags, string tag) =>
        tags.Any(candidate => string.Equals(candidate, tag.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void ApplyDerivations(BookAnnotationContext context, IEnumerable<HighlightItemDto> items)
    {
        foreach (var item in items)
        {
            if (context.Tree.Contexts.TryGetValue(item.Id, out var chapter))
            {
                item.ChapterId = chapter.ChapterId;
                item.ChapterPath = [.. chapter.ChapterPath];
            }

            if (context.Stitches.Memberships.TryGetValue(item.Id, out var stitch))
            {
                item.StitchGroupId = stitch.GroupId;
                item.StitchIndex = stitch.Index;
                item.StitchedText = stitch.StitchedText;
            }

            if (context.Tags.TryGetValue(item.Id, out var tags))
                item.Tags = [.. tags];
        }
    }

    private async Task AttachDerivedContextAsync(int userId, IReadOnlyCollection<HighlightItemDto> items)
    {
        if (items.Count == 0)
            return;

        var bookIds = items.Select(item => item.BookId).Distinct().ToList();
        var derivations = await annotationContext.GetDerivationsAsync(userId, bookIds);

        foreach (var item in items)
        {
            if (derivations.Chapters.TryGetValue(item.Id, out var context))
            {
                item.ChapterId = context.ChapterId;
                item.ChapterPath = [.. context.ChapterPath];
            }
            else
            {
                item.ChapterPath = [];
            }

            if (derivations.Stitches.Memberships.TryGetValue(item.Id, out var stitch))
            {
                item.StitchGroupId = stitch.GroupId;
                item.StitchIndex = stitch.Index;
                item.StitchedText = stitch.StitchedText;
            }

            if (derivations.Tags.TryGetValue(item.Id, out var tags))
                item.Tags = [.. tags];
        }
    }

    /// <summary>
    /// Lists every tag of the library. Spellings are grouped case-insensitively and each group
    /// reports the spelling of its lowest annotation id, so the list is stable across a re-import.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <returns>Tags ordered by annotation count descending, then by lower-cased name.</returns>
    public async Task<TagsResponse> GetTagsAsync(int userId)
    {
        var rows = await connection.QueryAsync<TagRow>(
            """
            SELECT id AS Id, tags AS Tags
            FROM highlights
            WHERE user_id = @UserId
              AND tags IS NOT NULL
              AND tags <> ''
            ORDER BY id ASC
            """,
            new { UserId = userId });

        var summaries = new Dictionary<string, TagSummaryDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            foreach (var tag in (row.Tags ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (summaries.TryGetValue(tag, out var summary))
                {
                    summary.Count++;
                }
                else
                {
                    summaries[tag] = new TagSummaryDto { Name = tag, Count = 1 };
                }
            }
        }

        return new TagsResponse
        {
            Tags =
            [
                .. summaries.Values
                    .OrderByDescending(summary => summary.Count)
                    .ThenBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase),
            ],
        };
    }

    private sealed class TagRow
    {
        public int Id { get; set; }
        public string? Tags { get; set; }
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
