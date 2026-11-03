using Quartz;
using Relego.Server.Sync;

namespace Relego.Server.Jobs;

/// <summary>
/// The daily inactivity sweep. It is the only sync-related Quartz job: routine syncing is driven by
/// the extension's own alarm, so the server never schedules a sync of its own.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SyncReminderJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncReminderJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reminders = scope.ServiceProvider.GetRequiredService<SyncReminderService>();

            var created = await reminders.RunAsync(context.CancellationToken);

            logger.LogInformation("Inactivity sweep raised {Count} reminder(s).", created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SyncReminderJob failed");
        }
    }
}
