using Microsoft.Data.Sqlite;
using Relego.Core.Parsing;

namespace Relego.Server.Infrastructure.Database;

public sealed class SchemaBootstrap
{
    /// <summary>Identifies the note-split and author-identity migration in the schema history.</summary>
    private const string NoteSplitMigrationId = "012-note-split-and-identity";

    /// <summary>The exact prefix older imports used to store a reader's note as a second highlight.</summary>
    private const string LegacyNotePrefix = "[my note] ";

    /// <summary>The author name older imports wrote when a source reported none.</summary>
    private const string LegacyUnknownAuthor = "Unknown Author";

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS users (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            kindle_email   TEXT    NOT NULL UNIQUE,
            delivery_email TEXT    NULL,
            created_at     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS authors (
            id   INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT    NULL
        );

        CREATE TABLE IF NOT EXISTS books (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id   INTEGER NOT NULL REFERENCES users(id),
            author_id INTEGER NOT NULL REFERENCES authors(id),
            title     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS highlights (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id         INTEGER NOT NULL REFERENCES users(id),
            book_id         INTEGER NOT NULL REFERENCES books(id),
            text            TEXT    NOT NULL,
            weight          INTEGER NOT NULL DEFAULT 3 CHECK(weight BETWEEN 1 AND 5),
            excluded        INTEGER NOT NULL DEFAULT 0,
            last_seen       TEXT    NULL,
            delivery_count  INTEGER NOT NULL DEFAULT 0,
            created_at      TEXT    NOT NULL,
            note            TEXT    NULL,
            heading_level   INTEGER NULL CHECK(heading_level IS NULL OR heading_level BETWEEN 1 AND 3),
            stitch_level    INTEGER NULL CHECK(stitch_level IS NULL OR stitch_level >= 1),
            tags            TEXT    NULL,
            source_position TEXT    NULL,
            position_start  INTEGER NULL,
            position_kind   TEXT    NULL CHECK(position_kind IS NULL OR position_kind IN ('location', 'page')),
            added_on        TEXT    NULL,
            CHECK(length(text) > 0 OR note IS NOT NULL)
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

        CREATE UNIQUE INDEX IF NOT EXISTS uq_authors_unknown
            ON authors(coalesce(name, '')) WHERE name IS NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS uq_books_user_author_title
            ON books(user_id, author_id, title);

        CREATE TABLE IF NOT EXISTS schema_migrations (
            id         TEXT PRIMARY KEY,
            applied_at TEXT NOT NULL
        );

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
            failure_diagnostics  TEXT    NULL,
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

        CREATE TABLE IF NOT EXISTS sync_book_highlights (
            user_id      INTEGER NOT NULL REFERENCES users(id),
            source_id    TEXT    NOT NULL,
            region_id    TEXT    NOT NULL,
            external_key TEXT    NOT NULL,
            highlight_id INTEGER NOT NULL REFERENCES highlights(id),
            PRIMARY KEY (user_id, source_id, region_id, external_key, highlight_id)
        );

        CREATE TABLE IF NOT EXISTS sync_job_activity (
            job_id           INTEGER PRIMARY KEY REFERENCES sync_jobs(id),
            user_id          INTEGER NOT NULL REFERENCES users(id),
            last_activity_at TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_connections_user_provider ON sync_connections(user_id, provider_id) WHERE state <> 'disconnected';
        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_jobs_user_idempotency ON sync_jobs(user_id, idempotency_key);
        CREATE UNIQUE INDEX IF NOT EXISTS uq_highlight_provenance_external ON highlight_provenance(source_id, region_id, external_id) WHERE external_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_reminders_connection_quiet ON sync_reminders(connection_id, quiet_since);
        """;

    /// <summary>Creates the schema and applies every pending migration.</summary>
    /// <param name="connection">Open connection to the database to upgrade.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <param name="logger">Logger that records what the migrations changed, or <c>null</c>.</param>
    public async Task ApplyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // A database with no schema at all is a fresh install: the schema below already is the
        // migrated shape, so the upgrade migrations below have nothing to record and must leave
        // schema_migrations empty.
        var isFreshInstall = !await TableExistsAsync(connection, "authors", cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Migration: add delivery_email column if it doesn't exist (existing databases)
        await MigrateAddDeliveryEmailColumnAsync(connection, cancellationToken);

        // Migration: keep the structural diagnostics a failure was reported with (existing databases)
        await EnsureColumnAsync(connection, "sync_jobs", "failure_diagnostics", "TEXT NULL", cancellationToken);

        // Migrations: the columns the running-job migration writes, for a database that predates them
        await EnsureColumnAsync(connection, "sync_jobs", "failure_code", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "sync_jobs", "ended_at", "TEXT NULL", cancellationToken);

        // Migrations: the annotation columns, for a database that predates them
        await EnsureColumnAsync(connection, "highlights", "note", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "heading_level", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "stitch_level", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "tags", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "source_position", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "position_start", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "position_kind", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "highlights", "added_on", "TEXT NULL", cancellationToken);

        // Migration: the annotation identity replaces the passage-only identity, legacy notes move
        // out of the passage and the unknown author stops being a real name (existing databases)
        await MigrateNoteSplitAndIdentityAsync(connection, logger, isFreshInstall, cancellationToken);

        // Migration: keep at most one running job per connection (existing databases)
        await MigrateSingleRunningJobAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Applies the note-split, author-identity and annotation-index migration exactly once.
    /// </summary>
    /// <param name="connection">Open connection to the database to upgrade.</param>
    /// <param name="logger">Logger that records what the migration changed, or <c>null</c>.</param>
    /// <param name="isFreshInstall">Whether the database had no schema before this run.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <remarks>
    /// The authors table is rebuilt here, so foreign keys have to be switched off outside the
    /// transaction (SQLite ignores the pragma inside one) and restored unconditionally afterwards:
    /// a half-applied upgrade must not leave the connection able to write orphans.
    /// </remarks>
    private static async Task MigrateNoteSplitAndIdentityAsync(
        SqliteConnection connection,
        ILogger? logger,
        bool isFreshInstall,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = OFF;", cancellationToken);

        try
        {
            await using var transaction = connection.BeginTransaction();

            if (isFreshInstall)
            {
                // A fresh database is already in the target shape, so only the annotation identity
                // has to be created. Nothing is recorded in schema_migrations: upgrade tracking
                // starts empty on a database this process created from scratch.
                await MigrateAnnotationIdentityAsync(connection, transaction, cancellationToken);
            }
            else if (!await IsMigrationAppliedAsync(connection, transaction, cancellationToken))
            {
                var splitNotes = await SplitLegacyNotesAsync(connection, transaction, cancellationToken);
                var mergedAuthors = await RebuildAuthorsAsync(connection, transaction, cancellationToken);
                await MigrateAnnotationIdentityAsync(connection, transaction, cancellationToken);

                await using var markCommand = connection.CreateCommand();
                markCommand.Transaction = transaction;
                markCommand.CommandText = """
                    INSERT INTO schema_migrations (id, applied_at) VALUES (@Id, @AppliedAt);
                    """;
                markCommand.Parameters.AddWithValue("@Id", NoteSplitMigrationId);
                markCommand.Parameters.AddWithValue("@AppliedAt", DateTimeOffset.UtcNow.ToString("o"));
                await markCommand.ExecuteNonQueryAsync(cancellationToken);

                logger?.LogInformation(
                    "Applied schema migration {MigrationId}: {SplitNotes} legacy note row(s) split, " +
                    "{MergedAuthors} duplicate unknown author row(s) merged",
                    NoteSplitMigrationId,
                    splitNotes,
                    mergedAuthors);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", CancellationToken.None);
        }
    }

    private static async Task<bool> IsMigrationAppliedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE id = @Id;";
        command.Parameters.AddWithValue("@Id", NoteSplitMigrationId);

        var applied = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(applied, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Moves the text of a legacy <c>[my note] </c> row into its note column.</summary>
    /// <returns>The number of rows that were split.</returns>
    private static async Task<int> SplitLegacyNotesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var legacyRows = new List<(long Id, string Text)>();

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = """
                SELECT id, text FROM highlights
                WHERE substr(text, 1, @PrefixLength) = @Prefix
                ORDER BY id;
                """;
            selectCommand.Parameters.AddWithValue("@Prefix", LegacyNotePrefix);
            selectCommand.Parameters.AddWithValue("@PrefixLength", LegacyNotePrefix.Length);

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                legacyRows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        foreach (var (id, text) in legacyRows)
        {
            var remainder = text[LegacyNotePrefix.Length..];
            var markers = AnnotationMarkerParser.Parse(remainder);

            // A note that is nothing but markers leaves no prose; the raw remainder is kept so a
            // row that only carried a note still carries one, exactly as the import path does.
            var note = markers.Note ?? (string.IsNullOrWhiteSpace(remainder) ? null : remainder.Trim());

            await using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE highlights
                SET text          = '',
                    note          = @Note,
                    heading_level = @HeadingLevel,
                    stitch_level  = @StitchLevel,
                    tags          = @Tags
                WHERE id = @Id;
                """;
            updateCommand.Parameters.AddWithValue("@Id", id);
            updateCommand.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("@HeadingLevel", (object?)markers.HeadingLevel ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("@StitchLevel", (object?)markers.StitchLevel ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue(
                "@Tags",
                markers.Tags.Count == 0 ? DBNull.Value : string.Join(' ', markers.Tags));
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return legacyRows.Count;
    }

    /// <summary>
    /// Rebuilds <c>authors</c> so a name can be absent, folding every sentinel row into the single
    /// row whose name is <c>NULL</c>.
    /// </summary>
    /// <returns>The number of duplicate sentinel rows that were folded away.</returns>
    private static async Task<int> RebuildAuthorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = transaction;
            createCommand.CommandText = """
                CREATE TABLE authors_migration (
                    id   INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT    NULL
                );

                INSERT INTO authors_migration (id, name)
                SELECT id, CASE WHEN name = @Sentinel THEN NULL ELSE name END
                FROM authors;
                """;
            createCommand.Parameters.AddWithValue("@Sentinel", LegacyUnknownAuthor);
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var unknownIds = new List<long>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = "SELECT id FROM authors_migration WHERE name IS NULL ORDER BY id;";
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                unknownIds.Add(reader.GetInt64(0));
        }

        if (unknownIds.Count > 1)
        {
            var survivorId = unknownIds[0];
            await using var mergeCommand = connection.CreateCommand();
            mergeCommand.Transaction = transaction;
            mergeCommand.CommandText = """
                UPDATE books SET author_id = @SurvivorId WHERE author_id = @DuplicateId;
                UPDATE excluded_authors SET author_id = @SurvivorId WHERE author_id = @DuplicateId;
                DELETE FROM authors_migration WHERE id = @DuplicateId;
                """;
            mergeCommand.Parameters.AddWithValue("@SurvivorId", survivorId);
            mergeCommand.Parameters.AddWithValue("@DuplicateId", 0L);

            foreach (var duplicateId in unknownIds.Skip(1))
            {
                mergeCommand.Parameters["@DuplicateId"].Value = duplicateId;
                await mergeCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using var swapCommand = connection.CreateCommand();
        swapCommand.Transaction = transaction;
        swapCommand.CommandText = """
            DROP TABLE authors;
            ALTER TABLE authors_migration RENAME TO authors;

            CREATE UNIQUE INDEX uq_authors_name ON authors(name);
            CREATE UNIQUE INDEX uq_authors_unknown ON authors(coalesce(name, '')) WHERE name IS NULL;
            """;
        await swapCommand.ExecuteNonQueryAsync(cancellationToken);

        return Math.Max(0, unknownIds.Count - 1);
    }

    /// <summary>Replaces the passage-only highlight identity with the annotation identity.</summary>
    private static async Task MigrateAnnotationIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP INDEX IF EXISTS uq_highlights_user_book_text;

            CREATE UNIQUE INDEX IF NOT EXISTS uq_highlights_user_book_passage
                ON highlights(user_id, book_id, text, coalesce(source_position, ''))
                WHERE text <> '';

            CREATE UNIQUE INDEX IF NOT EXISTS uq_highlights_user_book_note
                ON highlights(user_id, book_id, note)
                WHERE text = '';

            CREATE INDEX IF NOT EXISTS idx_highlights_user_book_order
                ON highlights(user_id, book_id, position_start, added_on, id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Tells whether the database already holds a table, which a fresh file does not.</summary>
    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @Name;";
        command.Parameters.AddWithValue("@Name", table);

        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Adds a column to an existing table when the database predates it.</summary>
    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
                return;
        }

        await reader.CloseAsync();

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MigrateSingleRunningJobAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE sync_jobs
            SET status = 'interrupted', failure_code = 'interrupted',
                ended_at = @Now
            WHERE status = 'running' AND id NOT IN (
                SELECT MAX(id) FROM sync_jobs WHERE status = 'running' GROUP BY connection_id
            );
            CREATE UNIQUE INDEX IF NOT EXISTS uq_sync_jobs_running
                ON sync_jobs(connection_id) WHERE status = 'running';
            """;
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    /// <summary>Creates the schema and applies every pending migration to the database at a path.</summary>
    /// <param name="dbPath">Path of the database file, created when missing.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <param name="logger">Logger that records what the migrations changed, or <c>null</c>.</param>
    public async Task ApplyAsync(
        string dbPath,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
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

        await ApplyAsync(connection, cancellationToken, logger);
    }
}
