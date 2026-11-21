using System.Globalization;
using Microsoft.Data.Sqlite;
using Relego.Server.Infrastructure.Database;

namespace Relego.Tests.Infrastructure;

/// <summary>
/// Covers the one-time upgrade of a database that predates reading annotations: the legacy
/// <c>[my note] </c> prefix is split out of <c>text</c>, the generated "Unknown Author" row becomes
/// the single unnamed author, reader state and provenance survive, and the guard table makes the
/// migration run exactly once.
/// </summary>
public sealed class SchemaBootstrapMigrationTests
{
    private const string PreFeatureSchemaSql = """
        CREATE TABLE users (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            kindle_email   TEXT    NOT NULL UNIQUE,
            delivery_email TEXT    NULL,
            created_at     TEXT    NOT NULL
        );

        CREATE TABLE authors (
            id   INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT    NOT NULL
        );

        CREATE TABLE books (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id   INTEGER NOT NULL REFERENCES users(id),
            author_id INTEGER NOT NULL REFERENCES authors(id),
            title     TEXT    NOT NULL
        );

        CREATE TABLE highlights (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id         INTEGER NOT NULL REFERENCES users(id),
            book_id         INTEGER NOT NULL REFERENCES books(id),
            text            TEXT    NOT NULL,
            weight          INTEGER NOT NULL DEFAULT 3 CHECK(weight BETWEEN 1 AND 5),
            excluded        INTEGER NOT NULL DEFAULT 0,
            last_seen       TEXT    NULL,
            delivery_count  INTEGER NOT NULL DEFAULT 0,
            created_at      TEXT    NOT NULL
        );

        CREATE TABLE excluded_books (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id     INTEGER NOT NULL REFERENCES users(id),
            book_id     INTEGER NOT NULL REFERENCES books(id),
            excluded_at TEXT    NOT NULL
        );

        CREATE TABLE excluded_authors (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id     INTEGER NOT NULL REFERENCES users(id),
            author_id   INTEGER NOT NULL REFERENCES authors(id),
            excluded_at TEXT    NOT NULL
        );

        CREATE TABLE settings (
            user_id       INTEGER PRIMARY KEY REFERENCES users(id),
            schedule      TEXT    NOT NULL DEFAULT 'weekly',
            delivery_day  TEXT    NULL,
            delivery_time TEXT    NOT NULL DEFAULT '18:00',
            count         INTEGER NOT NULL DEFAULT 3 CHECK(count BETWEEN 1 AND 15),
            timezone      TEXT    NOT NULL DEFAULT 'UTC'
        );

        CREATE TABLE recap_jobs (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id       INTEGER NOT NULL REFERENCES users(id),
            scheduled_for TEXT    NOT NULL,
            status        TEXT    NOT NULL DEFAULT 'pending',
            attempt_count INTEGER NOT NULL DEFAULT 0,
            error_message TEXT    NULL,
            created_at    TEXT    NOT NULL,
            delivered_at  TEXT    NULL
        );

        CREATE TABLE smtp_settings (
            id           INTEGER PRIMARY KEY CHECK (id = 1),
            host         TEXT    NOT NULL,
            port         INTEGER NOT NULL,
            from_address TEXT    NOT NULL,
            username     TEXT    NOT NULL DEFAULT '',
            password     TEXT    NOT NULL DEFAULT '',
            updated_at   TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX uq_authors_name
            ON authors(name);

        CREATE UNIQUE INDEX uq_books_user_author_title
            ON books(user_id, author_id, title);

        CREATE UNIQUE INDEX uq_recap_jobs_user_slot
            ON recap_jobs(user_id, scheduled_for);

        CREATE UNIQUE INDEX uq_highlights_user_book_text
            ON highlights(user_id, book_id, text);

        CREATE TABLE sync_connections (
            id                       INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id                  INTEGER NOT NULL REFERENCES users(id),
            provider_id              TEXT    NOT NULL,
            token_hash               TEXT    NOT NULL,
            state                    TEXT    NOT NULL,
            disclosure_version       TEXT    NOT NULL,
            profile_version          TEXT    NULL,
            region_id                TEXT    NULL,
            region_changed_at        TEXT    NULL,
            sync_interval_minutes    INTEGER NOT NULL DEFAULT 60 CHECK(sync_interval_minutes IN (15,30,45,60,120,240,360,720,1080,1440)),
            schedule_updated_at      TEXT    NULL,
            applied_interval_minutes INTEGER NULL,
            next_sync_due_at         TEXT    NULL,
            pending_command          TEXT    NULL,
            connected_at             TEXT    NULL,
            last_heartbeat_at        TEXT    NULL,
            last_sync_started_at     TEXT    NULL,
            last_sync_completed_at   TEXT    NULL,
            last_new_content_at      TEXT    NULL,
            last_auth_expired_at     TEXT    NULL,
            created_at               TEXT    NOT NULL
        );

        CREATE TABLE sync_jobs (
            id                   INTEGER PRIMARY KEY AUTOINCREMENT,
            connection_id        INTEGER NOT NULL REFERENCES sync_connections(id),
            user_id              INTEGER NOT NULL REFERENCES users(id),
            idempotency_key      TEXT    NOT NULL,
            mode                 TEXT    NOT NULL,
            trigger              TEXT    NOT NULL,
            status               TEXT    NOT NULL,
            phase                TEXT    NULL,
            books_total          INTEGER NULL,
            books_done           INTEGER NULL,
            highlights_seen      INTEGER NOT NULL DEFAULT 0,
            highlights_new       INTEGER NOT NULL DEFAULT 0,
            highlights_duplicate INTEGER NOT NULL DEFAULT 0,
            truncated_books      INTEGER NOT NULL DEFAULT 0,
            failure_code         TEXT    NULL,
            failure_detail       TEXT    NULL,
            recovery_steps       TEXT    NULL,
            failure_diagnostics  TEXT    NULL,
            started_at           TEXT    NULL,
            ended_at             TEXT    NULL
        );

        CREATE TABLE sync_job_batches (
            job_id               INTEGER NOT NULL REFERENCES sync_jobs(id),
            batch_index          INTEGER NOT NULL,
            new_highlights       INTEGER NOT NULL DEFAULT 0,
            duplicate_highlights INTEGER NOT NULL DEFAULT 0,
            new_books            INTEGER NOT NULL DEFAULT 0,
            new_authors          INTEGER NOT NULL DEFAULT 0,
            received_at          TEXT    NOT NULL,
            PRIMARY KEY (job_id, batch_index)
        );

        CREATE TABLE highlight_provenance (
            highlight_id  INTEGER PRIMARY KEY REFERENCES highlights(id),
            connection_id INTEGER NULL REFERENCES sync_connections(id),
            source_id     TEXT    NOT NULL,
            region_id     TEXT    NULL,
            external_id   TEXT    NULL,
            truncated     INTEGER NOT NULL DEFAULT 0,
            location      TEXT    NULL,
            note          TEXT    NULL,
            color         TEXT    NULL,
            first_seen_at TEXT    NOT NULL,
            last_seen_at  TEXT    NULL
        );

        CREATE TABLE sync_reminders (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            connection_id  INTEGER NOT NULL REFERENCES sync_connections(id),
            user_id        INTEGER NOT NULL REFERENCES users(id),
            quiet_since    TEXT    NOT NULL,
            threshold_days INTEGER NOT NULL,
            created_at     TEXT    NOT NULL,
            emailed_at     TEXT    NULL,
            dismissed_at   TEXT    NULL,
            resolved_at    TEXT    NULL
        );

        CREATE TABLE sync_book_highlights (
            user_id      INTEGER NOT NULL REFERENCES users(id),
            source_id    TEXT    NOT NULL,
            region_id    TEXT    NOT NULL,
            external_key TEXT    NOT NULL,
            highlight_id INTEGER NOT NULL REFERENCES highlights(id),
            PRIMARY KEY (user_id, source_id, region_id, external_key, highlight_id)
        );

        CREATE TABLE sync_job_activity (
            job_id           INTEGER PRIMARY KEY REFERENCES sync_jobs(id),
            user_id          INTEGER NOT NULL REFERENCES users(id),
            last_activity_at TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX uq_sync_connections_user_provider ON sync_connections(user_id, provider_id) WHERE state <> 'disconnected';
        CREATE UNIQUE INDEX uq_sync_jobs_user_idempotency ON sync_jobs(user_id, idempotency_key);
        CREATE UNIQUE INDEX uq_highlight_provenance_external ON highlight_provenance(source_id, region_id, external_id) WHERE external_id IS NOT NULL;
        CREATE UNIQUE INDEX uq_sync_reminders_connection_quiet ON sync_reminders(connection_id, quiet_since);
        """;

    [Fact]
    public async Task ApplyAsync_LegacyNoteRow_SplitsPrefixIntoNote()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var (userId, bookId) = await SeedReaderAsync(connection);

        var noteRowId = await InsertLegacyHighlightAsync(
            connection, userId, bookId, "[my note] .h2 .prob A summary title");
        var passageRowId = await InsertLegacyHighlightAsync(
            connection, userId, bookId, "An untouched passage.");

        await new SchemaBootstrap().ApplyAsync(connection);

        var noteRow = await ReadHighlightAsync(connection, noteRowId);
        Assert.Equal(string.Empty, noteRow.Text);
        Assert.Equal("A summary title", noteRow.Note);
        Assert.Equal(2, noteRow.HeadingLevel);
        Assert.Equal("prob", noteRow.Tags);

        var passageRow = await ReadHighlightAsync(connection, passageRowId);
        Assert.Equal("An untouched passage.", passageRow.Text);
        Assert.Null(passageRow.Note);
        Assert.Null(passageRow.HeadingLevel);
    }

    [Fact]
    public async Task ApplyAsync_SentinelAuthor_BecomesTheSingleNullAuthor()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var userId = await SeedUserAsync(connection);
        var sentinelAuthorId = await SeedAuthorAsync(connection, "Unknown Author");
        var bookId = await SeedBookAsync(connection, userId, sentinelAuthorId, "A Book");

        await new SchemaBootstrap().ApplyAsync(connection);

        Assert.Null(await ReadAuthorNameAsync(connection, sentinelAuthorId));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM authors WHERE name IS NULL;"));
        Assert.Equal(sentinelAuthorId, await ReadBookAuthorIdAsync(connection, bookId));
    }

    [Fact]
    public async Task ApplyAsync_PreservesCurationAndIdentity()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var (userId, bookId) = await SeedReaderAsync(connection);

        var highlightId = await InsertLegacyHighlightAsync(
            connection,
            userId,
            bookId,
            "A curated passage.",
            weight: 5,
            excluded: true,
            deliveryCount: 4,
            lastSeen: "2026-03-01T00:00:00Z",
            createdAt: "2025-05-01T00:00:00Z");
        await InsertProvenanceAsync(connection, highlightId);
        var countBefore = await CountAsync(connection, "SELECT COUNT(*) FROM highlights;");

        await new SchemaBootstrap().ApplyAsync(connection);

        var row = await ReadHighlightAsync(connection, highlightId);
        Assert.Equal("A curated passage.", row.Text);
        Assert.Equal(5, row.Weight);
        Assert.True(row.Excluded);
        Assert.Equal(4, row.DeliveryCount);
        Assert.Equal("2026-03-01T00:00:00Z", row.LastSeen);
        Assert.Equal("2025-05-01T00:00:00Z", row.CreatedAt);

        Assert.Equal(
            1,
            await CountAsync(
                connection,
                "SELECT COUNT(*) FROM highlight_provenance WHERE highlight_id = $highlightId;",
                ("$highlightId", highlightId)));
        Assert.Equal(countBefore, await CountAsync(connection, "SELECT COUNT(*) FROM highlights;"));
    }

    [Fact]
    public async Task ApplyAsync_SecondRun_IsANoOp()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var (userId, bookId) = await SeedReaderAsync(connection);
        await InsertLegacyHighlightAsync(connection, userId, bookId, "[my note] A legacy note");

        var schemaBootstrap = new SchemaBootstrap();
        await schemaBootstrap.ApplyAsync(connection);

        var laterRowId = await InsertLegacyHighlightAsync(
            connection, userId, bookId, "[my note] Written after the upgrade");

        await schemaBootstrap.ApplyAsync(connection);

        var laterRow = await ReadHighlightAsync(connection, laterRowId);
        Assert.Equal("[my note] Written after the upgrade", laterRow.Text);
        Assert.Null(laterRow.Note);
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM schema_migrations;"));
    }

    [Fact]
    public async Task ApplyAsync_WhenMigrationFails_RestoresForeignKeys()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var (userId, bookId) = await SeedReaderAsync(connection);

        var firstRowId = await InsertLegacyHighlightAsync(
            connection, userId, bookId, "[my note] .prob same");
        var secondRowId = await InsertLegacyHighlightAsync(
            connection, userId, bookId, "[my note] same");

        await Assert.ThrowsAnyAsync<Exception>(() => new SchemaBootstrap().ApplyAsync(connection));

        Assert.True(await ReadForeignKeysAsync(connection));
        Assert.Equal(0, await CountAsync(connection, "SELECT COUNT(*) FROM schema_migrations;"));
        Assert.Equal("[my note] .prob same", (await ReadHighlightAsync(connection, firstRowId)).Text);
        Assert.Equal("[my note] same", (await ReadHighlightAsync(connection, secondRowId)).Text);
    }

    [Fact]
    public async Task ApplyAsync_AfterSuccess_RestoresForeignKeys()
    {
        await using var connection = await CreatePreFeatureDatabaseAsync();
        var (userId, bookId) = await SeedReaderAsync(connection);
        await InsertLegacyHighlightAsync(connection, userId, bookId, "A passage.");

        await new SchemaBootstrap().ApplyAsync(connection);

        Assert.True(await ReadForeignKeysAsync(connection));
    }

    private static async Task<SqliteConnection> CreatePreFeatureDatabaseAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = PreFeatureSchemaSql;
            await schema.ExecuteNonQueryAsync();
        }

        return connection;
    }

    private static async Task<(long UserId, long BookId)> SeedReaderAsync(SqliteConnection connection)
    {
        var userId = await SeedUserAsync(connection);
        var authorId = await SeedAuthorAsync(connection, "An Author");
        var bookId = await SeedBookAsync(connection, userId, authorId, "A Book");

        return (userId, bookId);
    }

    private static async Task<long> SeedUserAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (kindle_email, created_at)
            VALUES ($kindleEmail, '2026-01-01T00:00:00Z');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$kindleEmail", "reader@kindle.com");

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> SeedAuthorAsync(SqliteConnection connection, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO authors (name) VALUES ($name);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> SeedBookAsync(
        SqliteConnection connection, long userId, long authorId, string title)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO books (user_id, author_id, title)
            VALUES ($userId, $authorId, $title);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$authorId", authorId);
        command.Parameters.AddWithValue("$title", title);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertLegacyHighlightAsync(
        SqliteConnection connection,
        long userId,
        long bookId,
        string text,
        int weight = 3,
        bool excluded = false,
        long deliveryCount = 0,
        string? lastSeen = null,
        string createdAt = "2025-01-01T00:00:00Z")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO highlights
                (user_id, book_id, text, weight, excluded, last_seen, delivery_count, created_at)
            VALUES
                ($userId, $bookId, $text, $weight, $excluded, $lastSeen, $deliveryCount, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$bookId", bookId);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$weight", weight);
        command.Parameters.AddWithValue("$excluded", excluded ? 1 : 0);
        command.Parameters.AddWithValue("$lastSeen", (object?)lastSeen ?? DBNull.Value);
        command.Parameters.AddWithValue("$deliveryCount", deliveryCount);
        command.Parameters.AddWithValue("$createdAt", createdAt);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task InsertProvenanceAsync(SqliteConnection connection, long highlightId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO highlight_provenance (highlight_id, source_id, first_seen_at)
            VALUES ($highlightId, 'kindle', '2025-05-01T00:00:00Z');
            """;
        command.Parameters.AddWithValue("$highlightId", highlightId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HighlightSnapshot> ReadHighlightAsync(SqliteConnection connection, long id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, note, heading_level, tags, weight, excluded, delivery_count,
                   last_seen, created_at
            FROM highlights
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Highlight {id} is missing after the migration.");

        return new HighlightSnapshot
        {
            Id = reader.GetInt64(0),
            Text = reader.GetString(1),
            Note = reader.IsDBNull(2) ? null : reader.GetString(2),
            HeadingLevel = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Tags = reader.IsDBNull(4) ? null : reader.GetString(4),
            Weight = reader.GetInt32(5),
            Excluded = reader.GetInt64(6) != 0,
            DeliveryCount = reader.GetInt64(7),
            LastSeen = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = reader.GetString(9),
        };
    }

    private static async Task<string?> ReadAuthorNameAsync(SqliteConnection connection, long authorId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM authors WHERE id = $id;";
        command.Parameters.AddWithValue("$id", authorId);

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    private static async Task<long> ReadBookAuthorIdAsync(SqliteConnection connection, long bookId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT author_id FROM books WHERE id = $id;";
        command.Parameters.AddWithValue("$id", bookId);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ReadForeignKeysAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed record HighlightSnapshot
    {
        public long Id { get; init; }

        public required string Text { get; init; }

        public string? Note { get; init; }

        public int? HeadingLevel { get; init; }

        public string? Tags { get; init; }

        public int Weight { get; init; }

        public bool Excluded { get; init; }

        public long DeliveryCount { get; init; }

        public string? LastSeen { get; init; }

        public required string CreatedAt { get; init; }
    }
}
