using Microsoft.AspNetCore.Mvc;
using Relego.Core.Contracts;
using Relego.Server.Data;
using Relego.Server.Models;
using Relego.Server.Services;
using Relego.Server.Sync;

namespace Relego.Server.Endpoints;

public static class StatusEndpoints
{
    private static readonly string ServerVersion =
        typeof(StatusEndpoints).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public static WebApplication MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/status", async (
            [FromServices] UserRepository userRepo,
            [FromServices] StatusRepository statusRepo,
            [FromServices] ISchedulerService schedulerService,
            [FromServices] IEnumerable<ICloudSyncProvider> providers,
            [FromServices] SyncConnectionRepository connections,
            [FromServices] SyncJobRepository jobs,
            [FromServices] SyncReminderRepository reminders,
            [FromServices] SyncService syncService) =>
        {
            var userId = await userRepo.EnsureUserAsync();
            var user = await userRepo.GetByIdAsync(userId);
            var status = await statusRepo.GetStatusAsync(userId);

            var nextFire = schedulerService.GetNextFireTimeUtc();
            status.NextRecap = nextFire?.ToString("O");
            status.KindleEmailConfigured = !string.IsNullOrWhiteSpace(user.KindleEmail);
            status.DeliveryEmailConfigured = !string.IsNullOrWhiteSpace(user.DeliveryEmail);
            status.ServerVersion = ServerVersion;
            status.KindleCloud = await ReadKindleCloudAsync(userId, providers, connections, jobs, reminders, syncService);

            return Results.Ok(status);
        })
        .WithTags("Status")
        .WithSummary("Get server status.")
        .WithDescription("Returns aggregate counts for highlights, books, authors, and exclusions for the implicit MVP user, along with the next scheduled recap time and the Kindle cloud sync state.")
        .Produces<StatusResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>
    /// Reads the Kindle cloud state for the CLI. It is <c>null</c> only when the server has no
    /// Kindle cloud provider registered; a user without a connection reads as <c>not_connected</c>.
    /// </summary>
    private static async Task<KindleCloudStatus?> ReadKindleCloudAsync(
        int userId,
        IEnumerable<ICloudSyncProvider> providers,
        SyncConnectionRepository connections,
        SyncJobRepository jobs,
        SyncReminderRepository reminders,
        SyncService syncService)
    {
        if (providers.FirstOrDefault(provider => provider.Descriptor.Id == KindleCloudProvider.ProviderId) is not { } provider)
            return null;

        // Same recovery sweep as the sync status endpoint: a job whose browser went away must read
        // as interrupted rather than as a sync that never ends.
        await syncService.MarkStaleJobsAsync();

        var connection = await connections.GetLiveAsync(userId, provider.Descriptor.Id);
        var currentJob = connection is null ? null : await jobs.GetRunningAsync(connection.Id);
        var latestJob = connection is null ? null : await jobs.GetLatestTerminalAsync(connection.Id);
        var lastCompletedJob = connection is null ? null : await jobs.GetLatestCompletedAsync(connection.Id);
        var reminder = connection is null ? null : await reminders.GetActiveAsync(connection.Id);

        var now = DateTimeOffset.UtcNow;
        var status = SyncStatusDerivation.Derive(connection, currentJob, latestJob, reminder is not null, now);
        var progressJob = currentJob ?? lastCompletedJob;

        return new KindleCloudStatus
        {
            ProviderId = provider.Descriptor.Id,
            Status = status,
            RegionId = connection?.RegionId,
            SyncIntervalMinutes = connection?.SyncIntervalMinutes,
            BooksDone = progressJob?.BooksDone,
            BooksTotal = progressJob?.BooksTotal,
            HighlightsNew = lastCompletedJob?.HighlightsNew,
            LastSyncCompletedAt = connection?.LastSyncCompletedAt,
            FailureDetail = MapFailureDetail(status, latestJob)
        };
    }

    private static string? MapFailureDetail(string status, SyncJobRecord? latestJob)
    {
        if (status is not ("failed" or "auth_expired"))
            return null;

        if (latestJob?.Status is "failed" or "auth_expired" or "interrupted")
        {
            var code = latestJob.FailureCode ?? latestJob.Status;
            return latestJob.FailureDetail ?? SyncFailureRecovery.DetailFor(code);
        }

        return status == "auth_expired" ? SyncFailureRecovery.DetailFor("auth_expired") : null;
    }
}
