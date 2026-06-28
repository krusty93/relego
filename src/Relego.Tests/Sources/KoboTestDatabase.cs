using Microsoft.Data.Sqlite;

namespace Relego.Tests.Sources;

/// <summary>
/// Builds throwaway Kobo-shaped SQLite databases for tests. The committed fixture
/// (<c>docs/examples/kobo-highlights.sqlite</c>) contains only plain highlights and
/// must not be regenerated (FR-016), so scenario rows (notes, hidden, dogear,
/// orphaned, UTF-8, performance) are produced here on temp files.
/// </summary>
internal static class KoboTestDatabase
{
    internal sealed record BookRow(string ContentId, string Title, string? Author);

    internal sealed record BookmarkRow(
        string VolumeId,
        string? Text,
        string? Annotation = null,
        string Type = "highlight",
        string? DateCreated = "2022-01-01T10:00:00.000",
        string? Hidden = "false");

    /// <summary>
    /// Creates a temp SQLite file with <c>content</c> and <c>Bookmark</c> tables populated
    /// from the given rows. Returns the path; the caller is responsible for deleting it.
    /// </summary>
    public static string Create(
        IEnumerable<BookRow> books,
        IEnumerable<BookmarkRow> bookmarks,
        bool createBookmarkTable = true,
        bool createContentTable = true)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "relego-kobotest-" + Guid.NewGuid().ToString("N") + ".sqlite");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (var transaction = connection.BeginTransaction())
            {
                if (createContentTable)
                {
                    Execute(connection, "CREATE TABLE content (ContentID TEXT PRIMARY KEY, Title TEXT, Attribution TEXT);");
                    foreach (var book in books)
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = "INSERT INTO content (ContentID, Title, Attribution) VALUES ($id, $title, $author);";
                        cmd.Parameters.AddWithValue("$id", book.ContentId);
                        cmd.Parameters.AddWithValue("$title", book.Title);
                        cmd.Parameters.AddWithValue("$author", (object?)book.Author ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                if (createBookmarkTable)
                {
                    Execute(connection,
                        "CREATE TABLE Bookmark (VolumeID TEXT, Text TEXT, Annotation TEXT, Type TEXT, DateCreated TEXT, Hidden TEXT);");
                    foreach (var bookmark in bookmarks)
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText =
                            "INSERT INTO Bookmark (VolumeID, Text, Annotation, Type, DateCreated, Hidden) " +
                            "VALUES ($vol, $text, $annotation, $type, $date, $hidden);";
                        cmd.Parameters.AddWithValue("$vol", bookmark.VolumeId);
                        cmd.Parameters.AddWithValue("$text", (object?)bookmark.Text ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$annotation", (object?)bookmark.Annotation ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$type", (object?)bookmark.Type ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$date", (object?)bookmark.DateCreated ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$hidden", (object?)bookmark.Hidden ?? DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        SqliteConnection.ClearAllPools();
        return path;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
