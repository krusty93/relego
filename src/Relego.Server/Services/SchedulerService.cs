using Quartz;
using Relego.Server.Jobs;
using Relego.Server.Models;

namespace Relego.Server.Services;

public sealed class SchedulerService(
    ISchedulerFactory schedulerFactory,
    ILogger<SchedulerService> logger) : ISchedulerService
{
    private static readonly JobKey RecapJobKey = new("RecapJob", "Recap");
    private static readonly TriggerKey RecapTriggerKey = new("RecapTrigger", "Recap");
    private static readonly JobKey ReminderJobKey = new("SyncReminderJob", "Sync");
    private static readonly TriggerKey ReminderTriggerKey = new("SyncReminderTrigger", "Sync");

    /// <summary>Daily at 03:00 UTC: the sweep is not user-facing, so it is off the recap schedule.</summary>
    private const string ReminderCronExpression = "0 0 3 * * ?";

    public async Task ScheduleAsync(Settings settings, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
        var cronExpression = BuildCronExpression(settings);

        var job = JobBuilder.Create<RecapJob>()
            .WithIdentity(RecapJobKey)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(RecapTriggerKey)
            .ForJob(RecapJobKey)
            .WithCronSchedule(cronExpression, x => x.InTimeZone(timeZone))
            .Build();

        await scheduler.AddJob(job, replace: true, cancellationToken);

        if (await scheduler.CheckExists(RecapTriggerKey, cancellationToken))
        {
            await scheduler.RescheduleJob(RecapTriggerKey, trigger, cancellationToken);
            logger.LogInformation("Rescheduled existing recap trigger");
        }
        else
        {
            await scheduler.ScheduleJob(trigger, cancellationToken);
        }

        var nextFire = trigger.GetNextFireTimeUtc();
        logger.LogInformation(
            "Scheduled recap: cron={Cron}, timezone={Tz}, nextFire={NextFire}",
            cronExpression, settings.Timezone, nextFire);
    }

    /// <inheritdoc />
    public async Task ScheduleReminderAsync(CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var job = JobBuilder.Create<SyncReminderJob>()
            .WithIdentity(ReminderJobKey)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(ReminderTriggerKey)
            .ForJob(ReminderJobKey)
            .WithCronSchedule(ReminderCronExpression, x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        await scheduler.AddJob(job, replace: true, cancellationToken);

        if (await scheduler.CheckExists(ReminderTriggerKey, cancellationToken))
        {
            await scheduler.RescheduleJob(ReminderTriggerKey, trigger, cancellationToken);
        }
        else
        {
            await scheduler.ScheduleJob(trigger, cancellationToken);
        }

        logger.LogInformation(
            "Scheduled inactivity sweep: cron={Cron}, nextFire={NextFire}",
            ReminderCronExpression,
            trigger.GetNextFireTimeUtc());
    }

    public DateTimeOffset? GetNextFireTimeUtc()
    {
        var scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
        var trigger = scheduler.GetTrigger(RecapTriggerKey).GetAwaiter().GetResult();
        return trigger?.GetNextFireTimeUtc();
    }

    internal static string BuildCronExpression(Settings settings)
    {
        var timeParts = settings.DeliveryTime.Split(':');
        var hour = timeParts[0];
        var minute = timeParts[1];

        if (settings.Schedule == "weekly" && !string.IsNullOrWhiteSpace(settings.DeliveryDay))
        {
            var dayOfWeek = NormalizeDayOfWeek(settings.DeliveryDay);
            return $"0 {minute} {hour} ? * {dayOfWeek}";
        }

        return $"0 {minute} {hour} * * ?";
    }

    private static string NormalizeDayOfWeek(string day)
    {
        return day.ToUpperInvariant() switch
        {
            "MONDAY" => "MON",
            "TUESDAY" => "TUE",
            "WEDNESDAY" => "WED",
            "THURSDAY" => "THU",
            "FRIDAY" => "FRI",
            "SATURDAY" => "SAT",
            "SUNDAY" => "SUN",
            _ => day.ToUpperInvariant()[..3]
        };
    }
}
