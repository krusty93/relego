using System.Data;
using Dapper;
using Relego.Core.Contracts;

namespace Relego.Server.Data;

public sealed class HighlightRepository(IDbConnection connection)
{
    public async Task<HighlightsResponse> GetHighlightsAsync(int userId, int page, int pageSize, string? q)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(q);
        var filter = hasFilter ? $"%{q}%" : null;

        var countSql = hasFilter
            ? """
              SELECT COUNT(*)
              FROM highlights h
              INNER JOIN books b ON b.id = h.book_id
              INNER JOIN authors a ON a.id = b.author_id
              WHERE h.user_id = @UserId
                AND (h.text LIKE @Filter OR b.title LIKE @Filter OR a.name LIKE @Filter)
              """
            : "SELECT COUNT(*) FROM highlights WHERE user_id = @UserId";

        var itemSql = hasFilter
            ? """
              SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName
              FROM highlights h
              INNER JOIN books b ON b.id = h.book_id
              INNER JOIN authors a ON a.id = b.author_id
              WHERE h.user_id = @UserId
                AND (h.text LIKE @Filter OR b.title LIKE @Filter OR a.name LIKE @Filter)
              ORDER BY h.id ASC
              LIMIT @PageSize OFFSET @Offset
              """
            : """
              SELECT h.id AS Id, b.id AS BookId, a.id AS AuthorId, h.text AS Text, b.title AS BookTitle, a.name AS AuthorName
              FROM highlights h
              INNER JOIN books b ON b.id = h.book_id
              INNER JOIN authors a ON a.id = b.author_id
              WHERE h.user_id = @UserId
              ORDER BY h.id ASC
              LIMIT @PageSize OFFSET @Offset
              """;

        var param = new { UserId = userId, Filter = filter, PageSize = pageSize, Offset = (page - 1) * pageSize };

        var total = await connection.ExecuteScalarAsync<int>(countSql, param);
        var items = await connection.QueryAsync<HighlightItemDto>(itemSql, param);

        return new HighlightsResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items.AsList()
        };
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
