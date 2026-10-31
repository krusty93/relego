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
}
