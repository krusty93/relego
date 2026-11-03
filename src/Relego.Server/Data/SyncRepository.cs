using System.Data;
using Dapper;
using Relego.Core.Contracts;

namespace Relego.Server.Data;

public sealed class SyncRepository(IDbConnection connection)
{
    /// <summary>Author name stored when a source reports none; shared with provenance writes.</summary>
    /// <param name="author">Author as reported by the source, or <c>null</c>.</param>
    /// <returns>The author name used by the import path.</returns>
    public static string ResolveAuthorName(string? author) =>
        string.IsNullOrWhiteSpace(author) ? "Unknown Author" : author;

    public async Task<SyncResponse> ImportAsync(int userId, SyncRequest request)
    {
        var response = new SyncResponse();

        using var transaction = connection.BeginTransaction();

        foreach (var book in request.Books)
        {
            var authorName = ResolveAuthorName(book.Author);

            var newAuthors = await connection.ExecuteAsync(
                "INSERT OR IGNORE INTO authors (name) VALUES (@Name)",
                new { Name = authorName }, transaction);
            response.NewAuthors += newAuthors;

            var authorId = await connection.QuerySingleAsync<int>(
                "SELECT id FROM authors WHERE name = @Name",
                new { Name = authorName }, transaction);

            var newBooks = await connection.ExecuteAsync(
                "INSERT OR IGNORE INTO books (user_id, author_id, title) VALUES (@UserId, @AuthorId, @Title)",
                new { UserId = userId, AuthorId = authorId, Title = book.Title }, transaction);
            response.NewBooks += newBooks;

            var bookId = await connection.QuerySingleAsync<int>(
                "SELECT id FROM books WHERE user_id = @UserId AND author_id = @AuthorId AND title = @Title",
                new { UserId = userId, AuthorId = authorId, Title = book.Title }, transaction);

            foreach (var highlight in book.Highlights)
            {
                var inserted = await connection.ExecuteAsync(
                    """
                    INSERT OR IGNORE INTO highlights (user_id, book_id, text, weight, excluded, delivery_count, created_at)
                    VALUES (@UserId, @BookId, @Text, 3, 0, 0, @CreatedAt)
                    """,
                    new
                    {
                        UserId = userId,
                        BookId = bookId,
                        Text = highlight.Text,
                        CreatedAt = (highlight.AddedOn ?? DateTimeOffset.UtcNow).ToString("o")
                    }, transaction);

                if (inserted > 0)
                    response.NewHighlights++;
                else
                    response.DuplicateHighlights++;
            }
        }

        transaction.Commit();
        return response;
    }
}
