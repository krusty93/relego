using Microsoft.Data.Sqlite;
using Relego.Server.Infrastructure.Database;

namespace Relego.Tests.Infrastructure;

public class SchemaBootstrapTests
{
    [Fact]
    public async Task ApplyAsync_OnDatabasePredatingAColumn_AddsTheColumn()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"relego-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var dbPath = Path.Combine(testDirectory, "relego.db");

        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                await using var legacySchema = connection.CreateCommand();
                legacySchema.CommandText = """
                    CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, created_at TEXT NOT NULL DEFAULT '');
                    CREATE TABLE sync_jobs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        connection_id INTEGER NOT NULL,
                        user_id INTEGER NOT NULL,
                        idempotency_key TEXT NOT NULL,
                        status TEXT NOT NULL
                    );
                    """;
                await legacySchema.ExecuteNonQueryAsync();
            }

            await new SchemaBootstrap().ApplyAsync(dbPath);

            Assert.True(await HasColumnAsync(connectionString, "sync_jobs", "failure_diagnostics"));
            Assert.True(await HasColumnAsync(connectionString, "users", "delivery_email"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // File may still be locked by SQLite; cleanup will happen on next run
                }
            }
        }
    }

    private static async Task<bool> HasColumnAsync(string connectionString, string table, string column)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    [Fact]
    public async Task ApplyAsync_WhenRunTwice_DoesNotThrow()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"relego-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var dbPath = Path.Combine(testDirectory, "relego.db");

        try
        {
            var schemaBootstrap = new SchemaBootstrap();

            await schemaBootstrap.ApplyAsync(dbPath);
            var secondRunException = await Record.ExceptionAsync(() => schemaBootstrap.ApplyAsync(dbPath));

            Assert.Null(secondRunException);
            Assert.True(File.Exists(dbPath));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // File may still be locked by SQLite; cleanup will happen on next run
                }
            }
        }
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_CreatesTheAnnotationColumns()
    {
        await using var connection = await CreateDatabaseAsync();

        var columns = await ReadColumnsAsync(connection, "highlights");

        foreach (var expected in new[]
        {
            "note", "heading_level", "stitch_level", "tags",
            "source_position", "position_start", "position_kind", "added_on",
        })
        {
            Assert.Contains(expected, columns);
        }
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_CreatesTheAnnotationIdentityIndexes()
    {
        await using var connection = await CreateDatabaseAsync();

        var indexes = await ReadIndexNamesAsync(connection);

        Assert.Contains("uq_highlights_user_book_passage", indexes);
        Assert.Contains("uq_highlights_user_book_note", indexes);
        Assert.Contains("idx_highlights_user_book_order", indexes);
        Assert.Contains("uq_authors_unknown", indexes);
        Assert.DoesNotContain("uq_highlights_user_book_text", indexes);
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_CreatesAnEmptySchemaMigrationsTable()
    {
        await using var connection = await CreateDatabaseAsync();

        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM schema_migrations;"));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_AcceptsAnAuthorWithoutAName()
    {
        await using var connection = await CreateDatabaseAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO authors (name) VALUES (NULL);";
        var exception = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task ApplyAsync_FreshDatabase_RejectsAHeadingLevelOutsideOneToThree(int headingLevel)
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, "A passage.", headingLevel: headingLevel));

        Assert.Contains("CHECK constraint failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_AcceptsHeadingLevelsOneToThree()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        foreach (var headingLevel in new[] { 1, 2, 3 })
        {
            await InsertHighlightAsync(
                connection, userId, bookId, $"Passage {headingLevel}.", headingLevel: headingLevel);
        }

        Assert.Equal(3, await CountAsync(connection, "SELECT COUNT(*) FROM highlights;"));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_RejectsAZeroStitchLevel()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, "A passage.", stitchLevel: 0));

        Assert.Contains("CHECK constraint failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_RejectsAnUnknownPositionKind()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, "A passage.", positionKind: "chapter"));

        Assert.Contains("CHECK constraint failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("location")]
    [InlineData("page")]
    public async Task ApplyAsync_FreshDatabase_AcceptsTheKnownPositionKinds(string positionKind)
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        await InsertHighlightAsync(
            connection, userId, bookId, "A passage.", positionKind: positionKind, positionStart: 42);

        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM highlights;"));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_RejectsAnEmptyPassageWithoutANote()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, text: string.Empty));

        Assert.Contains("CHECK constraint failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_AcceptsAnEmptyPassageThatCarriesANote()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        await InsertHighlightAsync(connection, userId, bookId, text: string.Empty, note: "Only a note.");

        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM highlights;"));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_AllowsTheSamePassageAtDifferentPositions()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        await InsertHighlightAsync(
            connection, userId, bookId, "A repeated sentence.",
            positionKind: "location", sourcePosition: "Location 10", positionStart: 10);
        await InsertHighlightAsync(
            connection, userId, bookId, "A repeated sentence.",
            positionKind: "location", sourcePosition: "Location 20", positionStart: 20);

        Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM highlights;"));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_RejectsTheSamePassageAtTheSamePosition()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        await InsertHighlightAsync(
            connection, userId, bookId, "A repeated sentence.",
            positionKind: "location", sourcePosition: "Location 10", positionStart: 10);

        await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, "A repeated sentence.",
            positionKind: "location", sourcePosition: "Location 10", positionStart: 10));
    }

    [Fact]
    public async Task ApplyAsync_FreshDatabase_RejectsTwoNoteOnlyAnnotationsWithTheSameNote()
    {
        await using var connection = await CreateDatabaseAsync();
        var (userId, bookId) = await SeedBookAsync(connection);

        await InsertHighlightAsync(connection, userId, bookId, text: string.Empty, note: "Only a note.");

        await Assert.ThrowsAsync<SqliteException>(() => InsertHighlightAsync(
            connection, userId, bookId, text: string.Empty, note: "Only a note."));
    }

    [Fact]
    public async Task ApplyAsync_OnADatabaseWithAnnotations_KeepsTheIndexesOnASecondRun()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"relego-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var dbPath = Path.Combine(testDirectory, "relego.db");

        try
        {
            var schemaBootstrap = new SchemaBootstrap();
            await schemaBootstrap.ApplyAsync(dbPath);

            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                var (userId, bookId) = await SeedBookAsync(connection);
                await InsertHighlightAsync(connection, userId, bookId, "A passage.", note: "A note.");
            }

            var secondRunException = await Record.ExceptionAsync(() => schemaBootstrap.ApplyAsync(dbPath));

            Assert.Null(secondRunException);

            await using var verify = new SqliteConnection(connectionString);
            await verify.OpenAsync();
            var indexes = await ReadIndexNamesAsync(verify);
            Assert.Contains("uq_highlights_user_book_passage", indexes);
            Assert.Contains("uq_highlights_user_book_note", indexes);
            Assert.Equal(1, await CountAsync(verify, "SELECT COUNT(*) FROM highlights;"));
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // File may still be locked by SQLite; cleanup will happen on next run
                }
            }
        }
    }

    private static async Task<SqliteConnection> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        await new SchemaBootstrap().ApplyAsync(connection);
        return connection;
    }

    private static async Task<(long UserId, long BookId)> SeedBookAsync(SqliteConnection connection)
    {
        var userId = await InsertAndGetIdAsync(
            connection, "INSERT INTO users (kindle_email, created_at) VALUES ('reader@kindle.com', '2026-01-01T00:00:00Z');");
        var authorId = await InsertAndGetIdAsync(
            connection, "INSERT INTO authors (name) VALUES ('An Author');");

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO books (user_id, author_id, title) VALUES ($userId, $authorId, 'A Book');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$authorId", authorId);
        var bookId = Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

        return (userId, bookId);
    }

    private static async Task<long> InsertAndGetIdAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT last_insert_rowid();";
        var id = await idCommand.ExecuteScalarAsync();
        return Convert.ToInt64(id, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertHighlightAsync(
        SqliteConnection connection,
        long userId,
        long bookId,
        string text,
        string? note = null,
        int? headingLevel = null,
        int? stitchLevel = null,
        string? positionKind = null,
        string? tags = null,
        string? sourcePosition = null,
        int? positionStart = null,
        string? addedOn = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO highlights
                (user_id, book_id, text, created_at, note, heading_level, stitch_level,
                 tags, source_position, position_kind, position_start, added_on)
            VALUES
                ($userId, $bookId, $text, '2026-01-01T00:00:00Z', $note, $headingLevel, $stitchLevel,
                 $tags, $sourcePosition, $positionKind, $positionStart, $addedOn);
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$headingLevel", (object?)headingLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$stitchLevel", (object?)stitchLevel ?? DBNull.Value);
        command.Parameters.AddWithValue("$tags", (object?)tags ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourcePosition", (object?)sourcePosition ?? DBNull.Value);
        command.Parameters.AddWithValue("$positionKind", (object?)positionKind ?? DBNull.Value);
        command.Parameters.AddWithValue("$positionStart", (object?)positionStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$addedOn", (object?)addedOn ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadColumnsAsync(SqliteConnection connection, string table)
    {
        var columns = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        return columns;
    }

    private static async Task<List<string>> ReadIndexNamesAsync(SqliteConnection connection)
    {
        var indexes = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!await reader.IsDBNullAsync(0))
                indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
