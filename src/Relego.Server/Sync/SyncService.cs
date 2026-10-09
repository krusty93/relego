using System.Text.Json;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Models;

namespace Relego.Server.Sync;

/// <summary>
/// Orchestrates extension-initiated sync jobs: single-flight starts, idempotent batch commits routed
/// through the provider normalization into the shared <c>SyncRepository</c>, progress, completion,
/// failure and stale-job handling.
/// </summary>
public sealed class SyncService(
    SyncConnectionRepository connections,
    SyncJobRepository jobs,
    SyncRepository syncRepository,
    IEnumerable<ICloudSyncProvider> providers,
    ILogger<SyncService> logger)
{
    private const string ModeRoutine = "routine";
    private const string ModeFullResync = "full_resync";

    private static readonly string[] AllowedModes = [ModeRoutine, ModeFullResync];
    private static readonly string[] AllowedTriggers = ["scheduled", "manual", "initial", "retry", "region_change"];
    private static readonly TimeSpan StaleWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Starts a sync job, or returns the job already associated with the idempotency key. A second
    /// concurrent start returns the running job (single-flight); a full resync start while a job is
    /// running is rejected.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="providerId">Provider id.</param>
    /// <param name="request">The start request.</param>
    /// <returns>The started or reused job.</returns>
    public async Task<SyncJobStartResult> StartJobAsync(int userId, string providerId, SyncJobStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = ResolveProvider(providerId);

        if (!AllowedModes.Contains(request.Mode))
            throw new SyncOperationException($"mode must be one of: {string.Join(", ", AllowedModes)}.", StatusCodes.Status422UnprocessableEntity);

        if (!AllowedTriggers.Contains(request.Trigger))
            throw new SyncOperationException($"trigger must be one of: {string.Join(", ", AllowedTriggers)}.", StatusCodes.Status422UnprocessableEntity);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new SyncOperationException("idempotencyKey must not be empty.", StatusCodes.Status422UnprocessableEntity);

        var connection = await connections.GetLiveAsync(userId, providerId)
            ?? throw new SyncOperationException("No active connection for this provider.", StatusCodes.Status409Conflict);

        if (connection.State == "pending_pairing")
            throw new SyncOperationException("Pair the browser extension before starting a sync.", StatusCodes.Status409Conflict);

        var existing = await jobs.GetByKeyAsync(userId, request.IdempotencyKey);
        if (existing is not null)
            return new SyncJobStartResult { Job = existing, Reused = true };

        var running = await jobs.GetRunningAsync(connection.Id);
        if (running is not null)
        {
            if (request.Mode == ModeFullResync)
                throw new SyncOperationException($"A sync job ({running.Id}) is already running.", StatusCodes.Status409Conflict);

            return new SyncJobStartResult { Job = running, Reused = true };
        }

        if (request.Mode == ModeFullResync && connection.State != "active")
            throw new SyncOperationException("A full resync requires an active connection.", StatusCodes.Status409Conflict);

        var now = DateTimeOffset.UtcNow.ToString("o");
        var job = await jobs.InsertAsync(connection.Id, userId, request.IdempotencyKey, request.Mode, request.Trigger, now);
        await connections.RecordSyncStartedAsync(connection.Id, now);
        await connections.ClearAuthExpiredAsync(connection.Id);

        // A user-requested command is satisfied by this job rather than duplicated.
        await connections.ConsumePendingCommandAsync(connection.Id);

        logger.LogInformation(
            "Sync job {JobId} started for provider {ProviderId} ({Mode}/{Trigger}).",
            job.Id,
            provider.Descriptor.Id,
            job.Mode,
            job.Trigger);

        return new SyncJobStartResult { Job = job };
    }

    /// <summary>
    /// Commits a batch. Replays of the same batch index return the stored counts unchanged; a fresh
    /// batch is normalized by the provider and imported through the shared, insert-only path.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="providerId">Provider id.</param>
    /// <param name="jobId">Target job id.</param>
    /// <param name="batch">The raw provider batch.</param>
    /// <returns>The committed (or replayed) batch result.</returns>
    public async Task<SyncBatchResult> CommitBatchAsync(int userId, string providerId, long jobId, CloudSyncBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var provider = ResolveProvider(providerId);
        var job = await GetOwnedJobAsync(userId, jobId);

        if (job.Status != "running")
            throw new SyncOperationException($"Job {jobId} is already {job.Status}.", StatusCodes.Status409Conflict);

        var replayed = await jobs.GetBatchAsync(jobId, batch.BatchIndex);
        if (replayed is not null)
        {
            return new SyncBatchResult
            {
                BatchIndex = replayed.BatchIndex,
                Replayed = true,
                Response = new SyncResponse
                {
                    NewHighlights = replayed.NewHighlights,
                    DuplicateHighlights = replayed.DuplicateHighlights,
                    NewBooks = replayed.NewBooks,
                    NewAuthors = replayed.NewAuthors
                }
            };
        }

        var normalization = provider.Normalize(batch);
        if (!normalization.IsValid)
            throw new SyncOperationException(string.Join(" ", normalization.Errors), StatusCodes.Status422UnprocessableEntity);

        var request = provider.ToSyncRequest(normalization);
        var response = await syncRepository.ImportAsync(userId, request);

        await jobs.SaveBatchAsync(new SyncJobBatchRecord
        {
            JobId = jobId,
            BatchIndex = batch.BatchIndex,
            NewHighlights = response.NewHighlights,
            DuplicateHighlights = response.DuplicateHighlights,
            NewBooks = response.NewBooks,
            NewAuthors = response.NewAuthors,
            ReceivedAt = DateTimeOffset.UtcNow.ToString("o")
        });

        await jobs.AddCountersAsync(jobId, CountHighlights(batch), response.NewHighlights, response.DuplicateHighlights);

        return new SyncBatchResult
        {
            BatchIndex = batch.BatchIndex,
            Replayed = false,
            Response = response
        };
    }

    /// <summary>Updates the phase and, when supplied, the book progress counters of a running job.</summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="jobId">Target job id.</param>
    /// <param name="phase">Current phase.</param>
    /// <param name="booksTotal">Total books discovered, or <c>null</c>.</param>
    /// <param name="booksDone">Books processed so far, or <c>null</c>.</param>
    /// <returns>A task that completes when the progress has been persisted.</returns>
    public async Task UpdateProgressAsync(int userId, long jobId, string phase, int? booksTotal, int? booksDone)
    {
        var job = await GetOwnedJobAsync(userId, jobId);
        if (job.Status != "running")
            throw new SyncOperationException($"Job {jobId} is already {job.Status}.", StatusCodes.Status409Conflict);

        await jobs.UpdatePhaseAsync(jobId, phase, booksTotal, booksDone);
    }

    /// <summary>
    /// Completes a job. A terminal completion requires <paramref name="booksDone"/> to equal
    /// <paramref name="booksTotal"/>; otherwise the job is not completed.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="jobId">Target job id.</param>
    /// <param name="booksTotal">Total books.</param>
    /// <param name="booksDone">Books processed.</param>
    /// <param name="truncatedBooks">Books with an export-limit truncation warning.</param>
    /// <returns>The completed job.</returns>
    public async Task<SyncJobRecord> CompleteAsync(int userId, long jobId, int booksTotal, int booksDone, int truncatedBooks)
    {
        var job = await GetOwnedJobAsync(userId, jobId);
        if (job.Status != "running")
            throw new SyncOperationException($"Job {jobId} is already {job.Status}.", StatusCodes.Status409Conflict);

        if (booksDone != booksTotal)
            throw new SyncOperationException("booksDone must equal booksTotal to complete a job.", StatusCodes.Status422UnprocessableEntity);

        var hasChanges = job.HighlightsNew > 0;
        var status = hasChanges ? "completed_with_changes" : "completed_no_changes";
        var now = DateTimeOffset.UtcNow.ToString("o");

        await jobs.CompleteAsync(jobId, status, booksTotal, booksDone, truncatedBooks, now);
        await connections.RecordSyncCompletedAsync(job.ConnectionId, now, hasChanges ? now : null);

        return await jobs.GetByIdAsync(jobId)
            ?? throw new SyncOperationException($"Unknown job {jobId}.", StatusCodes.Status404NotFound);
    }

    /// <summary>Fails a job with a stable code, sanitized detail and recovery steps.</summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="jobId">Target job id.</param>
    /// <param name="code">Stable failure code.</param>
    /// <param name="detail">Sanitized failure detail, or <c>null</c>.</param>
    /// <param name="recoverySteps">Ordered recovery steps, or <c>null</c>.</param>
    /// <returns>A task that completes when the failure has been persisted.</returns>
    public async Task FailAsync(int userId, long jobId, string code, string? detail, IReadOnlyList<string>? recoverySteps)
    {
        var job = await GetOwnedJobAsync(userId, jobId);
        if (job.Status != "running")
            throw new SyncOperationException($"Job {jobId} is already {job.Status}.", StatusCodes.Status409Conflict);

        var status = string.Equals(code, "auth_expired", StringComparison.Ordinal) ? "auth_expired" : "failed";
        var now = DateTimeOffset.UtcNow.ToString("o");
        var steps = recoverySteps is { Count: > 0 } ? JsonSerializer.Serialize(recoverySteps) : null;

        await jobs.FailAsync(jobId, status, code, SanitizeDetail(detail), steps, now);

        if (status == "auth_expired")
            await connections.RecordAuthExpiredAsync(job.ConnectionId, now);
    }

    /// <summary>
    /// Marks running jobs that have made no progress within the fifteen-minute stale window as
    /// interrupted. Interrupted jobs never advance <c>last_sync_completed_at</c>.
    /// </summary>
    /// <returns>The number of jobs marked interrupted.</returns>
    public async Task<int> MarkStaleJobsAsync()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(StaleWindow).ToString("o");
        var stale = await jobs.GetStaleRunningAsync(cutoff);
        var now = DateTimeOffset.UtcNow.ToString("o");

        foreach (var job in stale)
        {
            await jobs.InterruptAsync(job.Id, now);
            logger.LogWarning("Sync job {JobId} marked interrupted after {Minutes} minutes without progress.", job.Id, StaleWindow.TotalMinutes);
        }

        return stale.Count;
    }

    private ICloudSyncProvider ResolveProvider(string providerId) =>
        providers.FirstOrDefault(provider => string.Equals(provider.Descriptor.Id, providerId, StringComparison.Ordinal))
        ?? throw new SyncOperationException($"Unknown provider '{providerId}'.", StatusCodes.Status404NotFound);

    private async Task<SyncJobRecord> GetOwnedJobAsync(int userId, long jobId)
    {
        var job = await jobs.GetByIdAsync(jobId);
        if (job is null || job.UserId != userId)
            throw new SyncOperationException($"Unknown job {jobId}.", StatusCodes.Status404NotFound);

        return job;
    }

    private static int CountHighlights(CloudSyncBatch batch)
    {
        var total = 0;
        var books = batch.Books;
        if (books is null)
            return 0;

        foreach (var book in books)
        {
            if (book?.Highlights is { } highlights)
                total += highlights.Count;
        }

        return total;
    }

    private static string? SanitizeDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        var trimmed = detail.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }
}

/// <summary>The outcome of starting or reusing a sync job.</summary>
public sealed record SyncJobStartResult
{
    /// <summary>The started or reused job.</summary>
    public SyncJobRecord Job { get; init; } = new();

    /// <summary>Known book fingerprints for routine-run skipping; empty in this foundational phase.</summary>
    public IReadOnlyList<SyncKnownBookDto> KnownBooks { get; init; } = [];

    /// <summary>Whether an existing job (by key or single-flight) was returned instead of a new one.</summary>
    public bool Reused { get; init; }
}

/// <summary>The outcome of committing a batch.</summary>
public sealed record SyncBatchResult
{
    /// <summary>The committed or replayed batch index.</summary>
    public int BatchIndex { get; init; }

    /// <summary>Whether the batch was a replay of a previously stored index.</summary>
    public bool Replayed { get; init; }

    /// <summary>The stored import counts for the batch.</summary>
    public SyncResponse Response { get; init; } = new();
}
