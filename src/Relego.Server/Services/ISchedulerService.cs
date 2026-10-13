using Relego.Server.Models;

namespace Relego.Server.Services;

public interface ISchedulerService
{
    Task ScheduleAsync(Settings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the daily inactivity sweep. Unlike the recap trigger this is not user-scheduled,
    /// so it is (re)registered on every boot rather than only on first run.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the job and trigger are stored.</returns>
    Task ScheduleReminderAsync(CancellationToken cancellationToken = default);

    DateTimeOffset? GetNextFireTimeUtc();
}
