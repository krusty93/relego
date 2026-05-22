namespace Relego.Core.Contracts;

public sealed class RecapTriggerResponse
{
    public string Status { get; init; } = string.Empty;

    public DateTimeOffset ScheduledFor { get; init; }
}
