using Dapper;
using Microsoft.Data.Sqlite;
using Relego.Server.Data;
using Relego.Server.Infrastructure.Database;
using Relego.Server.Sync;

namespace Relego.Tests.Infrastructure;

public sealed class SyncJobRepositoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartOrReuseAsync_ConcurrentConnectionsCreateExactlyOneJob(bool sameKey)
    {
        var path = Path.GetTempFileName();
        try
        {
            await SeedAsync(path);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 6).Select(index => Task.Run(async () =>
            {
                using var connection = Open(path);
                var jobs = new SyncJobRepository(connection);
                await gate.Task;
                return await jobs.StartOrReuseAsync(1, 1, sameKey ? "same" : $"key-{index}", "routine", "scheduled", DateTimeOffset.UtcNow.ToString("o"));
            })).ToArray();
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Single(results.Select(result => result.Job.Id).Distinct());
            Assert.Single(results, result => !result.Reused);
            using var db = Open(path);
            Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs WHERE status = 'running'"));
            Assert.Null(await db.QuerySingleAsync<string?>("SELECT pending_command FROM sync_connections"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StartOrReuseAsync_ConcurrentFullResyncConflictsButRoutineReuses()
    {
        var path = Path.GetTempFileName();
        try
        {
            await SeedAsync(path);
            using var db = Open(path);
            var first = await new SyncJobRepository(db).StartOrReuseAsync(1, 1, "first", "routine", "initial", DateTimeOffset.UtcNow.ToString("o"));
            var routines = Task.Run(async () =>
            {
                using var connection = Open(path);
                return await new SyncJobRepository(connection).StartOrReuseAsync(1, 1, "routine", "routine", "scheduled", DateTimeOffset.UtcNow.ToString("o"));
            });
            var full = Task.Run(async () =>
            {
                using var connection = Open(path);
                return await Assert.ThrowsAsync<SyncOperationException>(() =>
                    new SyncJobRepository(connection).StartOrReuseAsync(1, 1, "full", "full_resync", "manual", DateTimeOffset.UtcNow.ToString("o")));
            });
            await Task.WhenAll(routines, full);
            Assert.Equal(first.Job.Id, (await routines).Job.Id);
            Assert.Equal(409, (await full).StatusCode);
            Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_jobs"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bootstrap_ReconcilesOldRunningDuplicatesWithoutDroppingBatchHistory(bool pathOverload)
    {
        var path = Path.GetTempFileName();
        try
        {
            await SeedAsync(path);
            using (var db = Open(path))
            {
                await db.ExecuteAsync("""
                    DROP INDEX uq_sync_jobs_running;
                    INSERT INTO sync_jobs (connection_id, user_id, idempotency_key, mode, trigger, status, started_at)
                    VALUES (1, 1, 'old', 'routine', 'initial', 'running', '2026-01-01T00:00:00Z'),
                           (1, 1, 'new', 'routine', 'scheduled', 'running', '2026-01-02T00:00:00Z');
                    INSERT INTO sync_job_batches (job_id, batch_index, new_highlights, received_at)
                    VALUES (1, 0, 7, '2026-01-01T00:01:00Z');
                    """);
            }

            var bootstrap = new SchemaBootstrap();
            if (pathOverload)
                await bootstrap.ApplyAsync(path);
            else
            {
                using var db = Open(path);
                await bootstrap.ApplyAsync(db);
            }

            using var result = Open(path);
            Assert.Equal("interrupted", await result.QuerySingleAsync<string>("SELECT status FROM sync_jobs WHERE id = 1"));
            Assert.Equal("running", await result.QuerySingleAsync<string>("SELECT status FROM sync_jobs WHERE id = 2"));
            Assert.Equal(7, await result.QuerySingleAsync<int>("SELECT new_highlights FROM sync_job_batches"));
            await Assert.ThrowsAsync<SqliteException>(() => new SyncJobRepository(result).InsertAsync(1, 1, "third", "routine", "scheduled", DateTimeOffset.UtcNow.ToString("o")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InterruptAsync_RechecksActivityAfterStaleScan()
    {
        var path = Path.GetTempFileName();
        try
        {
            await SeedAsync(path);
            using var db = Open(path);
            var repo = new SyncJobRepository(db);
            var now = DateTimeOffset.UtcNow;
            var job = await repo.InsertAsync(1, 1, "stale", "routine", "initial", now.AddMinutes(-20).ToString("o"));
            var cutoff = now.AddMinutes(-15).ToString("o");
            Assert.Single(await repo.GetStaleRunningAsync(cutoff));
            using var otherDb = Open(path);
            await new SyncJobRepository(otherDb).UpdatePhaseAsync(job.Id, "reading", 2, 1, now.ToString("o"));
            Assert.Equal(0, await repo.InterruptAsync(job.Id, now.ToString("o"), cutoff));
            Assert.Equal("running", (await repo.GetByIdAsync(job.Id))!.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InterruptedJob_CannotBeRevivedByLateProgressCompletionOrFailure()
    {
        var path = Path.GetTempFileName();
        try
        {
            await SeedAsync(path);
            using var db = Open(path);
            var repo = new SyncJobRepository(db);
            var now = DateTimeOffset.UtcNow;
            var job = await repo.InsertAsync(1, 1, "stale", "routine", "initial", now.AddMinutes(-20).ToString("o"));
            Assert.Equal(1, await repo.InterruptAsync(job.Id, now.ToString("o"), now.AddMinutes(-15).ToString("o")));
            Assert.Equal(0, await repo.UpdatePhaseAsync(job.Id, "reading", 1, 1, now.ToString("o")));
            Assert.Equal(0, await repo.CompleteAsync(job.Id, "completed_no_changes", 1, 1, 0, now.ToString("o")));
            Assert.Equal(0, await repo.FailAsync(job.Id, "failed", "network", null, null, now.ToString("o")));
            Assert.Equal("interrupted", (await repo.GetByIdAsync(job.Id))!.Status);
            Assert.Equal(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sync_job_activity"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static async Task SeedAsync(string path)
    {
        using var db = Open(path);
        await new SchemaBootstrap().ApplyAsync(db);
        await db.ExecuteAsync("""
            INSERT INTO users (id, kindle_email, created_at) VALUES (1, 'reader@kindle.test', '2026-01-01');
            INSERT INTO sync_connections (id, user_id, provider_id, token_hash, state, disclosure_version, created_at, pending_command)
            VALUES (1, 1, 'kindle-cloud', 'test-hash', 'active', '1', '2026-01-01', 'sync');
            """);
    }
}
