using System.Data;
using Dapper;
using Relego.Core.Annotations;
using Relego.Core.Contracts;
using Relego.Core.Parsing;

namespace Relego.Server.Data;

public sealed class SyncRepository(IDbConnection connection)
{
    /// <summary>Author name stored when a source reports none: the single author row with no name.</summary>
    /// <param name="author">Author as reported by the source, or <c>null</c>.</param>
    /// <returns>The author as reported, or <c>null</c> when the source reported none.</returns>
    public static string? ResolveAuthorName(string? author) =>
        string.IsNullOrWhiteSpace(author) ? null : author;

    public async Task<SyncResponse> ImportAsync(int userId, SyncRequest request)
    {
        var response = new SyncResponse();
        var importTime = DateTimeOffset.UtcNow.ToString("o");

        using var transaction = connection.BeginTransaction();

        foreach (var book in request.Books)
        {
            var authorName = ResolveAuthorName(book.Author);

            var newAuthors = await connection.ExecuteAsync(
                "INSERT OR IGNORE INTO authors (name) VALUES (@Name)",
                new { Name = authorName }, transaction);
            response.NewAuthors += newAuthors;

            var authorId = await connection.QuerySingleAsync<int>(
                "SELECT id FROM authors WHERE name IS @Name",
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
                var text = highlight.Text ?? string.Empty;
                var position = string.IsNullOrWhiteSpace(highlight.Position) ? null : highlight.Position.Trim();
                var markers = AnnotationMarkerParser.Parse(highlight.Note);

                // A note that is nothing but markers leaves no prose; on a note-only annotation the
                // raw note is kept so the row still carries something (a row needs a passage or a note).
                var note = markers.Note
                    ?? (text.Length == 0 && !string.IsNullOrWhiteSpace(highlight.Note) ? highlight.Note.Trim() : null);

                var sourcePosition = SourcePosition.Parse(position);
                var annotation = new
                {
                    UserId = userId,
                    BookId = bookId,
                    Text = text,
                    Note = note,
                    HeadingLevel = markers.HeadingLevel,
                    StitchLevel = markers.StitchLevel,
                    Tags = markers.Tags.Count == 0 ? null : string.Join(' ', markers.Tags),
                    Position = position,
                    PositionStart = sourcePosition.Start,
                    PositionKind = sourcePosition.Kind,
                    AddedOn = highlight.AddedOn?.ToString("o"),
                    CreatedAt = importTime
                };

                var existingId = await connection.QuerySingleOrDefaultAsync<int?>(
                    """
                    SELECT id FROM highlights
                    WHERE user_id = @UserId
                      AND book_id = @BookId
                      AND (
                            (text <> '' AND text = @Text AND coalesce(source_position, '') = coalesce(@Position, ''))
                         OR (text = '' AND @Text = '' AND coalesce(note, '') = coalesce(@Note, ''))
                          )
                    LIMIT 1
                    """,
                    annotation,
                    transaction);

                if (existingId is null)
                {
                    await connection.ExecuteAsync(
                        """
                        INSERT INTO highlights (user_id, book_id, text, weight, excluded, delivery_count, created_at,
                                                note, heading_level, stitch_level, tags, source_position, position_start,
                                                position_kind, added_on)
                        VALUES (@UserId, @BookId, @Text, 3, 0, 0, @CreatedAt,
                                @Note, @HeadingLevel, @StitchLevel, @Tags, @Position, @PositionStart,
                                @PositionKind, @AddedOn)
                        """,
                        annotation,
                        transaction);

                    response.NewHighlights++;
                    continue;
                }

                // Re-importing only refreshes what the source said about the annotation; the reader's
                // weight, exclusion, delivery count and last-seen date belong to Relego and are kept.
                // The note and the markers it carries are source-owned, so an absent value is an
                // answer: removing a marker clears the vocabulary it produced (SC-013, FR-043).
                await connection.ExecuteAsync(
                    """
                    UPDATE highlights
                    SET note            = @Note,
                        heading_level   = @HeadingLevel,
                        stitch_level    = @StitchLevel,
                        tags            = @Tags,
                        source_position = coalesce(@Position, source_position),
                        position_start  = coalesce(@PositionStart, position_start),
                        position_kind   = coalesce(@PositionKind, position_kind),
                        added_on        = coalesce(@AddedOn, added_on)
                    WHERE id = @Id
                    """,
                    new
                    {
                        annotation.UserId,
                        annotation.BookId,
                        annotation.Text,
                        annotation.Note,
                        annotation.HeadingLevel,
                        annotation.StitchLevel,
                        annotation.Tags,
                        annotation.Position,
                        annotation.PositionStart,
                        annotation.PositionKind,
                        annotation.AddedOn,
                        Id = existingId.Value
                    },
                    transaction);

                response.DuplicateHighlights++;
            }
        }

        transaction.Commit();
        return response;
    }
}
