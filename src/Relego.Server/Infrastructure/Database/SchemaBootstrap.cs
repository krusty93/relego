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

        CREATE TABLE IF NOT EXISTS sync_connections (
            id                       INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id                  INTEGER NOT NULL REFERENCES users(id),
            provider_id              TEXT    NOT NULL,
            token_hash               TEXT    NOT NULL,
            state                    TEXT    NOT NULL,
            disclosure_version       TEXT    NOT NULL,
            profile_version          TEXT    NULL,
            region_id                TEXT    NULL,
            region_changed_at        TEXT    NULL,
            sync_interval_minutes    INTEGER NOT NULL DEFAULT 360 CHECK(sync_interval_minutes IN (15,30,45,60,120,240,360,720,1080,1440)),
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

        CREATE TABLE IF NOT EXISTS sync_jobs (
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
            started_at           TEXT    NULL,
            ended_at             TEXT    NULL
        );

        CREATE TABLE IF NOT EXISTS sync_job_batches (
            job_id               INTEGER NOT NULL REFERENCES sync_jobs(id),
            batch_index          INTEGER NOT NULL,
            new_highlights       INTEGER NOT NULL DEFAULT 0,
            duplicate_highlights INTEGER NOT NULL DEFAULT 0,
            new_books            INTEGER NOT NULL DEFAULT 0,
            new_authors          INTEGER NOT NULL DEFAULT 0,
            received_at          TEXT    NOT NULL,
            PRIMARY KEY (job_id, batch_index)
        );

        CREATE TABLE IF NOT EXISTS highlight_provenance (
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

        CREATE TABLE IF NOT EXISTS sync_reminders (
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

        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_connections_user_provider ON sync_connections(user_id, provider_id) WHERE state <> 'disconnected';
        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_jobs_user_idempotency ON sync_jobs(user_id, idempotency_key);
        CREATE UNIQUE INDEX IF NOT EXISTS uq_highlight_provenance_external ON highlight_provenance(source_id, region_id, external_id) WHERE external_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_reminders_connection_quiet ON sync_reminders(connection_id, quiet_since);
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
