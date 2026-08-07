using System.Data;
using Dapper;
using Relego.Core.Contracts;

namespace Relego.Server.Data;

public sealed class BookRepository(IDbConnection connection)
{
    /// <summary>
    /// Returns a page of books with their highlight counts and exclusion state.
    /// </summary>
    public async Task<BooksResponse> GetBooksAsync(int userId, int page, int pageSize, string? query)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : $"%{query.Trim()}%";

        const string filter =
            """
            FROM books b
            JOIN authors a ON a.id = b.author_id
            WHERE b.user_id = @UserId
              AND (@Query IS NULL OR b.title LIKE @Query OR a.name LIKE @Query)
            """;

        var total = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) " + filter,
            new { UserId = userId, Query = normalizedQuery }).ConfigureAwait(false);

        var items = await connection.QueryAsync<BookItemDto>(
            $"""
             SELECT
                 b.id    AS Id,
                 b.title AS Title,
                 a.id    AS AuthorId,
                 a.name  AS AuthorName,
                 (SELECT COUNT(*) FROM highlights h
                  WHERE h.book_id = b.id AND h.user_id = @UserId) AS HighlightCount,
                 (SELECT COUNT(*) FROM highlights h
                  WHERE h.book_id = b.id AND h.user_id = @UserId AND h.excluded = 1) AS ExcludedHighlightCount,
                 EXISTS (SELECT 1 FROM excluded_books eb
                         WHERE eb.book_id = b.id AND eb.user_id = @UserId) AS Excluded,
                 EXISTS (SELECT 1 FROM excluded_authors ea
                         WHERE ea.author_id = a.id AND ea.user_id = @UserId) AS AuthorExcluded
             {filter}
             ORDER BY b.title COLLATE NOCASE
             LIMIT @PageSize OFFSET @Offset
             """,
            new
            {
                UserId = userId,
                Query = normalizedQuery,
                PageSize = pageSize,
                Offset = (page - 1) * pageSize,
            }).ConfigureAwait(false);

        return new BooksResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = [.. items],
        };
    }

    /// <summary>
    /// Renames a book title.
    /// Returns <see langword="true"/> when the book was found and updated,
    /// <see langword="false"/> when no book with <paramref name="bookId"/> belongs to <paramref name="userId"/>,
    /// and <see langword="null"/> when the new title already exists for the same author and user.
    /// </summary>
    public async Task<bool?> RenameAsync(int userId, int bookId, string newTitle)
    {
        // Verify the book exists and belongs to the user
        var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
            "SELECT author_id FROM books WHERE id = @BookId AND user_id = @UserId",
            new { BookId = bookId, UserId = userId }).ConfigureAwait(false);

        if (authorId is null)
            return false;

        // Check for a duplicate title under the same author
        var duplicate = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM books
            WHERE user_id = @UserId AND author_id = @AuthorId AND title = @Title AND id != @BookId
            """,
            new { UserId = userId, AuthorId = authorId.Value, Title = newTitle, BookId = bookId })
            .ConfigureAwait(false);

        if (duplicate > 0)
            return null;

        await connection.ExecuteAsync(
            "UPDATE books SET title = @Title WHERE id = @BookId AND user_id = @UserId",
            new { Title = newTitle, BookId = bookId, UserId = userId })
            .ConfigureAwait(false);

        return true;
    }
}
