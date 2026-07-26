using Microsoft.Data.Sqlite;

namespace Relego.Server.Infrastructure.Database;

public sealed class SchemaBootstrap
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS users (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            kindle_email   TEXT    NOT NULL UNIQUE,
            delivery_email TEXT    NULL,
            created_at     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS authors (
            id   INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS books (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id   INTEGER NOT NULL REFERENCES users(id),
            author_id INTEGER NOT NULL REFERENCES authors(id),
            title     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS highlights (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id        INTEGER NOT NULL REFERENCES users(id),
            book_id        INTEGER NOT NULL REFERENCES books(id),
            text           TEXT    NOT NULL,
            weight         INTEGER NOT NULL DEFAULT 3 CHECK(weight BETWEEN 1 AND 5),
            excluded       INTEGER NOT NULL DEFAULT 0,
            last_seen      TEXT    NULL,
            delivery_count INTEGER NOT NULL DEFAULT 0,
            created_at     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS excluded_books (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id     INTEGER NOT NULL REFERENCES users(id),
            book_id     INTEGER NOT NULL REFERENCES books(id),
            excluded_at TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS excluded_authors (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id     INTEGER NOT NULL REFERENCES users(id),
            author_id   INTEGER NOT NULL REFERENCES authors(id),
            excluded_at TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS settings (
            user_id       INTEGER PRIMARY KEY REFERENCES users(id),
            schedule      TEXT    NOT NULL DEFAULT 'weekly',
            delivery_day  TEXT    NULL,
            delivery_time TEXT    NOT NULL DEFAULT '18:00',
            count         INTEGER NOT NULL DEFAULT 3 CHECK(count BETWEEN 1 AND 15),
            timezone      TEXT    NOT NULL DEFAULT 'UTC'
        );

        CREATE TABLE IF NOT EXISTS recap_jobs (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id       INTEGER NOT NULL REFERENCES users(id),
            scheduled_for TEXT    NOT NULL,
            status        TEXT    NOT NULL DEFAULT 'pending',
            attempt_count INTEGER NOT NULL DEFAULT 0,
            error_message TEXT    NULL,
            created_at    TEXT    NOT NULL,
            delivered_at  TEXT    NULL
        );

        CREATE TABLE IF NOT EXISTS smtp_settings (
            id           INTEGER PRIMARY KEY CHECK (id = 1),
            host         TEXT    NOT NULL,
            port         INTEGER NOT NULL,
            from_address TEXT    NOT NULL,
            username     TEXT    NOT NULL DEFAULT '',
            password     TEXT    NOT NULL DEFAULT '',
            updated_at   TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_authors_name
            ON authors(name);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_books_user_author_title
            ON books(user_id, author_id, title);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_highlights_user_book_text
            ON highlights(user_id, book_id, text);

        CREATE UNIQUE INDEX IF NOT EXISTS uq_recap_jobs_user_slot
            ON recap_jobs(user_id, scheduled_for);
        """;

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migration: add delivery_email column if it doesn't exist (existing databases)
        await MigrateAddDeliveryEmailColumnAsync(connection, cancellationToken);
    }

    private static async Task MigrateAddDeliveryEmailColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(users)";
        await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken);

        var hasDeliveryEmail = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(1);
            if (columnName == "delivery_email")
            {
                hasDeliveryEmail = true;
                break;
            }
        }

        if (!hasDeliveryEmail)
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE users ADD COLUMN delivery_email TEXT NULL";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task ApplyAsync(string dbPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
