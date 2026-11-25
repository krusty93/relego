namespace Relego.Server.Telemetry;

/// <summary>
/// Sends the daily heartbeat on the schedule described in ADR-010: once shortly after boot, then
/// once a day for the lifetime of the container.
/// </summary>
public sealed class TelemetryHeartbeatService(
    TelemetrySettings settings,
    TelemetryClient client,
    InstallationIdStore installationIds,
    TimeProvider timeProvider,
    ILogger<TelemetryHeartbeatService> logger) : BackgroundService
{
    /// <summary>How long the process waits before the first heartbeat, so boot traffic stays first.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    /// <summary>The interval between heartbeats.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.IsEnabled)
        {
            logger.LogInformation("Usage telemetry is off because {Reason}.", settings.DisabledReason);
            return;
        }

        using var timer = new PeriodicTimer(Interval, timeProvider);

        await BeatAsync(StartupDelay, stoppingToken).ConfigureAwait(false);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await BeatAsync(TimeSpan.Zero, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown: nothing to flush, the next boot sends the next heartbeat.
        }
    }

    private async Task BeatAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var installationId = await installationIds.GetOrCreateAsync(stoppingToken).ConfigureAwait(false);

        // A read-only or missing volume is not a failure worth a log line: the install simply stays uncounted.
        if (installationId is null)
            return;

        if (await client.SendAsync(settings, installationId, timeProvider, stoppingToken).ConfigureAwait(false))
            logger.LogDebug("Usage heartbeat sent.");
    }
}
